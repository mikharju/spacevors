using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Game;

public static class WorldRenderer
{
    private static readonly Dictionary<LitSprite, List<(Rectangle Dest, float AngleDeg)>> _litDraws = new();
    private static readonly List<(int X, int Y, int Radius)> _diagnosticCircles = new();

    private static readonly Color PlayerTurretColor = new(255, 180, 50, 255);

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
        DrawAmmo(em, camX, camY, windowWidth, windowHeight);
        DrawExplosions(em, camX, camY, windowWidth, windowHeight);
        DrawDamageSparks(em, camX, camY, windowWidth, windowHeight);
        DrawHealSparks(em, camX, camY, windowWidth, windowHeight);
        DrawSmokePuffs(em, camX, camY, windowWidth, windowHeight);
        ThrusterFlameRenderer.Draw(em, camX, camY, windowWidth, windowHeight);
        DrawPlayerShip(em, playerEntity, camX, camY, windowWidth, windowHeight, shipType, diagnostics);
        EnemyShipRenderer.Draw(em, camX, camY, windowWidth, windowHeight, diagnostics);
        DrawMines(em, camX, camY, windowWidth, windowHeight);
        TargetingRenderer.Draw(em, playerEntity, camX, camY, windowWidth, windowHeight);
        DrawXpPickups(em, camX, camY, windowWidth, windowHeight);
        DrawHealthOrbs(em, camX, camY, windowWidth, windowHeight);
        DrawTurrets(em, camX, camY, windowWidth, windowHeight);

        if (diagnostics)
        {
            DrawDebugMarkers(em, camX, camY, windowWidth, windowHeight);
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
            var (cx, cy) = RenderHelpers.ToScreen(pos.Value, camX, camY, windowWidth, windowHeight);

            if (RenderHelpers.IsOffScreen(cx, cy, ammo.Radius, windowWidth, windowHeight)) continue;

            Color color = ammo.Color switch
            {
                AmmoColor.Green => new Color(80, 255, 80, 255),
                AmmoColor.Blue => new Color(100, 180, 255, 255),
                AmmoColor.Red => new Color(255, 80, 80, 255),
                _ => new Color(255, 230, 100, 255)
            };

            Raylib.DrawCircle((int)cx, (int)cy, ammo.Radius, color);
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

            Raylib.DrawCircle((int)cx, (int)cy, Math.Max(currentRadius, 1f), color);
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

            Raylib.DrawCircle((int)cx, (int)cy, size, new Color(r, g, b, alpha));
        }
    }

    private static void DrawTurrets(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, turret, rot, pos) in em.GetEntitiesWithComponents<Turret, Rotation, Position>())
        {
            if (turret.IsEnemy) continue;

            var (cx, cy) = RenderHelpers.ToScreen(pos.Value, camX, camY, windowWidth, windowHeight);

            if (RenderHelpers.IsOffScreen(cx, cy, RenderHelpers.TurretMarkerSize / 2f, windowWidth, windowHeight)) continue;

            RenderHelpers.DrawTurretMarker(cx, cy, PlayerTurretColor);
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

            Raylib.DrawCircle((int)cx, (int)cy, size, new Color(50, 255, 100, alpha));
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
            Raylib.DrawCircle((int)cx, (int)cy, Math.Max(radius, 1f), new Color(SmokeColorR, SmokeColorG, SmokeColorB, alpha));
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

        ShipSpriteRenderer.DrawShipSprite(shipType, screenCx, screenCy, shipType.Radius * 2f, angleDeg);

        if (diagnostics) Raylib.DrawCircle((int)screenCx, (int)screenCy, shipType.Radius, new Color(0, 255, 0, 60));
    }
}
