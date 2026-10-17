using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Game;

public static class WorldRenderer
{
    private static readonly Dictionary<LitSprite, List<(Rectangle Dest, float AngleDeg, float Damage)>> _litDraws = new();
    private static readonly List<(int X, int Y, int Radius)> _diagnosticCircles = new();

    // Damage smoke: grey puffs that fade out over their lifetime.
    private const int SmokeColorR = 120;
    private const int SmokeColorG = 120;
    private const int SmokeColorB = 130;
    private const int SmokeAlphaMax = 80;

    // A mine down to its last hp is drawn fully opaque so it stands out; healthier ones stay dim
    // so the bright core reads as the mine body.
    private const int MineLastHealth = 1;
    private const int MineDimAlpha = 180;
    private const int MineOpaqueAlpha = 255;

    // Layer order, bottom to top. The deliberate choices:
    // - thruster flames draw before ships, so they appear behind the hull
    // - the player draws before enemy ships and mines: an overlapping enemy covers the player hull
    // - brackets, pickups and turret markers draw last, so they stay readable over sprites
    public static void Draw(
        EntityManager em, Entity playerEntity, ShipType shipType, bool diagnostics,
        float camX, float camY, int windowWidth, int windowHeight)
    {
        DrawAsteroids(em, camX, camY, windowWidth, windowHeight, diagnostics);
        RenderTimings.Pass("world.asteroids");
        DrawAmmo(em, camX, camY, windowWidth, windowHeight);
        RenderTimings.Pass("world.ammo");
        DrawLaserBeams(em, camX, camY, windowWidth, windowHeight);
        RenderTimings.Pass("world.beams");
        DrawExplosions(em, camX, camY, windowWidth, windowHeight);
        RenderTimings.Pass("world.explosions");
        DrawDamageSparks(em, camX, camY, windowWidth, windowHeight);
        RenderTimings.Pass("world.sparks");
        DrawHealSparks(em, camX, camY, windowWidth, windowHeight);
        RenderTimings.Pass("world.healSparks");
        DrawSmokePuffs(em, camX, camY, windowWidth, windowHeight);
        RenderTimings.Pass("world.smoke");
        ThrusterFlameRenderer.Draw(em, camX, camY, windowWidth, windowHeight);
        RenderTimings.Pass("world.thrusters");
        DrawPlayerShip(em, playerEntity, camX, camY, windowWidth, windowHeight, shipType, diagnostics);
        RenderTimings.Pass("world.player");
        EnemyShipRenderer.Draw(em, camX, camY, windowWidth, windowHeight, diagnostics);
        RenderTimings.Pass("world.enemyShips");
        DrawMines(em, camX, camY, windowWidth, windowHeight);
        RenderTimings.Pass("world.mines");
        TargetingRenderer.Draw(em, playerEntity, camX, camY, windowWidth, windowHeight);
        RenderTimings.Pass("world.targeting");
        DrawXpPickups(em, camX, camY, windowWidth, windowHeight);
        RenderTimings.Pass("world.xpPickups");
        DrawHealthOrbs(em, camX, camY, windowWidth, windowHeight);
        RenderTimings.Pass("world.healthOrbs");
        DrawTurrets(em, playerEntity, camX, camY, windowWidth, windowHeight);
        RenderTimings.Pass("world.turrets");

        if (diagnostics)
        {
            DrawDebugMarkers(em, camX, camY, windowWidth, windowHeight);
            RenderTimings.Pass("world.debugMarkers");
        }
    }

    private static void DrawAsteroids(EntityManager em, float camX, float camY, int windowWidth, int windowHeight, bool diagnostics)
    {
        LitGroupRenderer.Clear(_litDraws);
        _diagnosticCircles.Clear();

        foreach (var (entity, asteroid, rot, pos) in em.GetEntitiesWithComponents<Asteroid, Rotation, Position>())
        {
            var (cx, cy) = RenderHelpers.ToScreen(pos.Value, camX, camY, windowWidth, windowHeight);

            float angleDeg = rot.Angle * 180f / MathF.PI;
            AsteroidSprite? sprite = GetAsteroidSprite(asteroid);
            Texture2D? baseTex = sprite?.Lit?.Base ?? sprite?.Flat;

            float extent = baseTex.HasValue && baseTex.Value.Id != 0
                ? RenderHelpers.HalfDiagonal(asteroid.Radius * 2f, asteroid.Radius * 2f * (float)baseTex.Value.Height / baseTex.Value.Width)
                : RenderHelpers.HalfDiagonal(asteroid.Radius * 2f, asteroid.Radius * 2f);

            if (RenderHelpers.IsOffScreen(cx, cy, extent, windowWidth, windowHeight)) continue;

            if (diagnostics) _diagnosticCircles.Add(((int)cx, (int)cy, (int)asteroid.Radius));

            if (baseTex.HasValue && baseTex.Value.Id != 0)
                DrawAsteroidSprite(sprite, baseTex.Value, cx, cy, asteroid.Radius * 2f, angleDeg);
            else
                // Flat sprites draw straight away while lit ones wait for the batch flush below, so a
                // mixed layer draws flat variants under lit ones.
                Raylib.DrawRectanglePro(
                    new Rectangle((int)cx - (int)asteroid.Radius, (int)cy - (int)asteroid.Radius, (int)(asteroid.Radius * 2), (int)(asteroid.Radius * 2)),
                    new System.Numerics.Vector2(asteroid.Radius, asteroid.Radius),
                    angleDeg,
                    new Color(200, 200, 210, 255)
                );
        }

        LitGroupRenderer.Draw(_litDraws);

        if (diagnostics)
            foreach (var (x, y, radius) in _diagnosticCircles)
                Raylib.DrawCircle(x, y, radius, new Color(255, 0, 0, 60));
    }

    private static AsteroidSprite? GetAsteroidSprite(Asteroid asteroid)
    {
        var sprites = asteroid.IsSmall ? ImageLoader.AsteroidSmallSprites : ImageLoader.AsteroidLargeSprites;
        if (sprites == null || sprites.Length == 0) return null;
        // Wrap rather than give up: the domain picks a variant from its own constant count, which can
        // be out of step with how many sprite sets the asset directory holds.
        return sprites[asteroid.Variant % sprites.Length];
    }

    // Lit when the variant has normal + depth maps (collected for batched drawing), otherwise a flat texture.
    private static void DrawAsteroidSprite(AsteroidSprite? sprite, Texture2D baseTex, float cx, float cy, float diameter, float angleDeg)
    {
        float scale = diameter / baseTex.Width;
        var dest = new Rectangle(cx, cy, baseTex.Width * scale, baseTex.Height * scale);

        if (sprite?.Lit is { } lit)
            LitGroupRenderer.Add(_litDraws, lit, dest, angleDeg);
        else
            Raylib.DrawTexturePro(baseTex, RenderHelpers.FullSource(baseTex), dest, RenderHelpers.CenterOrigin(dest), angleDeg, Color.White);
    }

    private static void DrawAmmo(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, ammo, pos) in em.GetEntitiesWithComponents<Ammo, Position>())
        {
            // Same segment the collision system swept: fast bullets draw as streaks.
            Vector2 from = pos.Value - ammo.Velocity * GameSession.FixedDeltaTime;
            var (fromX, fromY) = RenderHelpers.ToScreen(from, camX, camY, windowWidth, windowHeight);
            var (toX, toY) = RenderHelpers.ToScreen(pos.Value, camX, camY, windowWidth, windowHeight);

            float extent = ammo.Radius;
            if (MathF.Max(fromX, toX) < -extent || MathF.Min(fromX, toX) > windowWidth + extent ||
                MathF.Max(fromY, toY) < -extent || MathF.Min(fromY, toY) > windowHeight + extent) continue;

            var color = AmmoColorOf(ammo.Color);
            float diameter = MathF.Max(1f, ammo.Radius * 2f);
            float streak = ammo.Velocity.Magnitude * GameSession.FixedDeltaTime;

            // A thick line shorter than it is wide draws as a bar across the bullet, so a bullet
            // that barely moves in one tick stays a round dot; only fast ones stretch into streaks.
            if (streak <= diameter) ShapeSprites.DrawCircle(toX, toY, ammo.Radius, color);
            else
                ShapeSprites.DrawStreak(
                    new System.Numerics.Vector2(fromX, fromY),
                    new System.Numerics.Vector2(toX, toY),
                    diameter,
                    color);
        }
    }

    public static Color AmmoColorOf(AmmoColor color) => color switch
    {
        AmmoColor.Green => new Color(80, 255, 80, 255),
        AmmoColor.Blue => new Color(100, 180, 255, 255),
        AmmoColor.Red => new Color(255, 80, 80, 255),
        AmmoColor.Cyan => new Color(90, 240, 240, 255),
        AmmoColor.Violet => new Color(200, 120, 255, 255),
        // Orange is a new identity channel (R14): a guided round, whatever hull fired it.
        AmmoColor.Orange => new Color(255, 150, 60, 255),
        _ => new Color(255, 230, 100, 255)
    };

    private static void DrawLaserBeams(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, beam) in em.GetEntitiesWithComponents<LaserBeam>())
        {
            Vector2 end = beam.End;
            var (x1, y1) = RenderHelpers.ToScreen(beam.Start, camX, camY, windowWidth, windowHeight);
            var (x2, y2) = RenderHelpers.ToScreen(end, camX, camY, windowWidth, windowHeight);

            if (MathF.Max(x1, x2) < 0f || MathF.Min(x1, x2) > windowWidth ||
                MathF.Max(y1, y2) < 0f || MathF.Min(y1, y2) > windowHeight) continue;

            int alpha = (int)(255f * Math.Clamp(beam.Lifetime / beam.InitialLifetime, 0f, 1f));
            Color baseColor = AmmoColorOf(beam.Color);
            var color = new Color(baseColor.R, baseColor.G, baseColor.B, (byte)alpha);

            Raylib.DrawLineEx(
                new System.Numerics.Vector2(x1, y1),
                new System.Numerics.Vector2(x2, y2),
                MathF.Max(1f, beam.Width * 2f),
                color);
        }
    }

    private static void DrawExplosions(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, explosion, pos) in em.GetEntitiesWithComponents<Explosion, Position>())
        {
            var (cx, cy) = RenderHelpers.ToScreen(pos.Value, camX, camY, windowWidth, windowHeight);

            float lifeRatio = explosion.Lifetime / explosion.InitialLifetime;
            float currentRadius = explosion.CurrentRadius;

            if (RenderHelpers.IsOffScreen(cx, cy, currentRadius, windowWidth, windowHeight)) continue;

            int alpha = (int)(255f * lifeRatio);

            Color color;
            if (lifeRatio > 0.7f)
            {
                color = new Color(255, 255, 180, alpha);
            }
            else if (lifeRatio > 0.4f)
            {
                float t = (lifeRatio - 0.4f) / 0.3f;
                color = new Color(
                    255,
                    (int)(140 + t * 60),
                    (int)(30 + t * 20),
                    alpha);
            }
            else
            {
                color = new Color(255, 140, 30, alpha);
            }

            ShapeSprites.DrawCircle(cx, cy, Math.Max(currentRadius, 1f), color);
        }
    }

    private static void DrawDamageSparks(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, spark, pos) in em.GetEntitiesWithComponents<DamageSpark, Position>())
        {
            var (cx, cy) = RenderHelpers.ToScreen(pos.Value, camX, camY, windowWidth, windowHeight);

            float lifeRatio = spark.Lifetime / spark.InitialLifetime;
            float size = Math.Max(lifeRatio * 5f, 1f);

            if (RenderHelpers.IsOffScreen(cx, cy, size, windowWidth, windowHeight)) continue;

            int r = (int)(lifeRatio * 255);
            int g = (int)(lifeRatio * lifeRatio * 80);
            int b = 0;
            int alpha = (int)(lifeRatio * 255);

            ShapeSprites.DrawCircle(cx, cy, size, new Color(r, g, b, alpha));
        }
    }

    private static void DrawTurrets(EntityManager em, Entity playerEntity, float camX, float camY, int windowWidth, int windowHeight)
    {
        var hullPos = em.GetComponent<Position>(playerEntity).Value;
        float hullRotation = em.GetComponent<Rotation>(playerEntity).Angle;

        foreach (var (entity, slot) in em.GetEntitiesWithComponents<TurretSlot>())
        {
            bool mounted = em.TryGetComponent<Turret>(entity, out _);
            // A mounted turret rides with the hull via SyncTurrets; an empty mount is just its slot.
            Vector2 world = mounted
                ? em.GetComponent<Position>(entity).Value
                : slot.Definition.WorldPosition(hullPos, hullRotation);

            var (cx, cy) = RenderHelpers.ToScreen(world, camX, camY, windowWidth, windowHeight);
            float radius = RenderHelpers.TurretMarkerRadius(slot.Definition.Size);
            if (RenderHelpers.IsOffScreen(cx, cy, RenderHelpers.TurretMarkerExtent(slot.Definition.Size), windowWidth, windowHeight)) continue;

            if (!mounted)
            {
                Raylib.DrawCircle((int)cx, (int)cy, radius, Palette.TurretEmptySlot);
                continue;
            }

            RenderHelpers.DrawTurretMarker(cx, cy, radius, Angle.ToDeg(em.GetComponent<Rotation>(entity).Angle), Palette.TurretMounted);
        }
    }

    private static void DrawMines(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        Texture2D? mineTex = ImageLoader.MineTexture;
        bool hasTexture = mineTex.HasValue && mineTex.Value.Id != 0;

        foreach (var (entity, mine, pos) in em.GetEntitiesWithComponents<EnemyMine, Position>())
        {
            var (cx, cy) = RenderHelpers.ToScreen(pos.Value, camX, camY, windowWidth, windowHeight);

            float extent = hasTexture
                ? RenderHelpers.HalfDiagonal(mine.Radius * 2f, mine.Radius * 2f * (float)mineTex!.Value.Height / mineTex.Value.Width)
                : mine.Radius;

            if (RenderHelpers.IsOffScreen(cx, cy, extent, windowWidth, windowHeight)) continue;

            bool hasHealth = em.TryGetComponent<Health>(entity, out var mineHealth);
            int healthAlpha = hasHealth && mineHealth.Current > MineLastHealth ? MineDimAlpha : MineOpaqueAlpha;

            if (hasTexture)
            {
                var tex = mineTex!.Value;
                float drawDiameter = mine.Radius * 2f;
                float scale = drawDiameter / tex.Width;
                float destWidth = tex.Width * scale;
                float destHeight = tex.Height * scale;

                Raylib.DrawTexturePro(
                    tex,
                    new Rectangle(0f, 0f, tex.Width, tex.Height),
                    new Rectangle(cx, cy, destWidth, destHeight),
                    new System.Numerics.Vector2(destWidth / 2f, destHeight / 2f),
                    0f,
                    Color.White
                );

                Raylib.DrawCircle((int)cx, (int)cy, mine.Radius * 0.4f, new Color(255, 200, 200, healthAlpha));
            }
            else
            {
                // No-texture fallback: red disc plus a bright core so mines stay readable.
                if (hasHealth)
                    Raylib.DrawCircle((int)cx, (int)cy, mine.Radius, new Color(255, 60, 60, healthAlpha));
                else
                    Raylib.DrawCircle((int)cx, (int)cy, mine.Radius, new Color(255, 100, 100, 200));

                Raylib.DrawCircle((int)cx, (int)cy, mine.Radius * 0.4f, new Color(255, 200, 200, 255));
            }
        }
    }

    private static void DrawXpPickups(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, pickup, pos) in em.GetEntitiesWithComponents<XpPickup, Position>())
        {
            var (cx, cy) = RenderHelpers.ToScreen(pos.Value, camX, camY, windowWidth, windowHeight);

            if (RenderHelpers.IsOffScreen(cx, cy, pickup.Radius, windowWidth, windowHeight)) continue;

            int alpha = pickup.Chased ? 255 : 180;
            Raylib.DrawCircle((int)cx, (int)cy, pickup.Radius, new Color(50, 150, 255, alpha));
        }
    }

    private static void DrawHealthOrbs(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, orb, pos) in em.GetEntitiesWithComponents<HealthOrb, Position>())
        {
            var (cx, cy) = RenderHelpers.ToScreen(pos.Value, camX, camY, windowWidth, windowHeight);

            if (RenderHelpers.IsOffScreen(cx, cy, orb.Radius, windowWidth, windowHeight)) continue;

            Raylib.DrawCircle((int)cx, (int)cy, orb.Radius, new Color(50, 255, 100, 200));
        }
    }

    private static void DrawHealSparks(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, spark, pos) in em.GetEntitiesWithComponents<HealSpark, Position>())
        {
            var (cx, cy) = RenderHelpers.ToScreen(pos.Value, camX, camY, windowWidth, windowHeight);

            float lifeRatio = spark.Lifetime / spark.InitialLifetime;
            float size = Math.Max(lifeRatio * 5f, 1f);

            if (RenderHelpers.IsOffScreen(cx, cy, size, windowWidth, windowHeight)) continue;

            int alpha = (int)(lifeRatio * 255);

            ShapeSprites.DrawCircle(cx, cy, size, new Color(50, 255, 100, alpha));
        }
    }

    private static void DrawSmokePuffs(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, puff, pos) in em.GetEntitiesWithComponents<SmokePuff, Position>())
        {
            var (cx, cy) = RenderHelpers.ToScreen(pos.Value, camX, camY, windowWidth, windowHeight);

            float lifeRatio = puff.Lifetime / puff.InitialLifetime;
            // Puffs grow to about twice their spawn radius as they fade out.
            float radius = puff.Radius * (1f + (1f - lifeRatio));

            if (RenderHelpers.IsOffScreen(cx, cy, radius, windowWidth, windowHeight)) continue;

            int alpha = (int)(SmokeAlphaMax * lifeRatio);
            ShapeSprites.DrawCircle(cx, cy, Math.Max(radius, 1f), new Color(SmokeColorR, SmokeColorG, SmokeColorB, alpha));
        }
    }

    private static void DrawDebugMarkers(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, marker, pos) in em.GetEntitiesWithComponents<DebugMarker, Position>())
        {
            var (cx, cy) = RenderHelpers.ToScreen(pos.Value, camX, camY, windowWidth, windowHeight);

            float lifeRatio = marker.Lifetime / marker.InitialLifetime;
            int alpha = (int)(255f * lifeRatio);
            int boxSize = 16;

            if (RenderHelpers.IsOffScreen(cx, cy, boxSize / 2f, windowWidth, windowHeight)) continue;

            Raylib.DrawRectangleLines((int)cx - boxSize / 2, (int)cy - boxSize / 2, boxSize, boxSize, new Color(0, 255, 0, alpha));
        }
    }

    private static void DrawPlayerShip(EntityManager em, Entity playerEntity, float camX, float camY, int windowWidth, int windowHeight, ShipType shipType, bool diagnostics)
    {
        var shipPos = em.GetComponent<Position>(playerEntity);
        var shipRot = em.GetComponent<Rotation>(playerEntity);

        var (screenCx, screenCy) = RenderHelpers.ToScreen(shipPos.Value, camX, camY, windowWidth, windowHeight);
        float angleDeg = shipRot.Angle * 180f / MathF.PI;

        float damage = em.TryGetComponent<Health>(playerEntity, out var health) ? DamageShading.FromHealth(health) : 0f;
        ShipSpriteRenderer.DrawShipSprite(shipType, screenCx, screenCy, shipType.Radius * 2f, angleDeg, damage);

        if (diagnostics) Raylib.DrawCircle((int)screenCx, (int)screenCy, shipType.Radius, new Color(0, 255, 0, 60));
    }
}
