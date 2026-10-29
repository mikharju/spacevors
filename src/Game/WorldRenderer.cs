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
        DrawMissiles(em, camX, camY, windowWidth, windowHeight);
        RenderTimings.Pass("world.missiles");
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

        if (ShowMissileBench)
        {
            DrawMissileBench();
            RenderTimings.Pass("world.missileBench");
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
            // A guided round is drawn as a body and an exhaust flame in its own pass: which way it is
            // pointed is the information the player has to read, and a streak cannot carry that.
            if (em.HasComponent<Missile>(entity)) continue;

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

    // A guided round: body, then the exhaust flame hanging off its aft end. The flame is drawn first so
    // the body sits on top of it, and both have floors — a missile the player cannot see is a missile
    // the player cannot dodge.
    private static void DrawMissiles(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (_, missile, ammo, pos) in em.GetEntitiesWithComponents<Missile, Ammo, Position>())
        {
            // Body length scales with the collision radius, so the 4 px pod round and the 6 px torpedo
            // read as the different objects their stats say they are.
            float bodyLength = ammo.Radius * MissileBodyStretch;
            var (cx, cy) = RenderHelpers.ToScreen(pos.Value, camX, camY, windowWidth, windowHeight);
            if (RenderHelpers.IsOffScreen(cx, cy, bodyLength * (1f + MissileExhaustHardTurn), windowWidth, windowHeight)) continue;

            float steering = Math.Clamp(missile.Steering, 0f, 1f);
            // A round still building to its card speed is burning for it, so the plume answers the throttle
            // as well as the rudder: full length just after launch, and again when the round swings hard.
            float boost = missile.MaxSpeed > 0f ? 1f - Math.Clamp(ammo.Velocity.Magnitude / missile.MaxSpeed, 0f, 1f) : 0f;
            float angle = Angle.Of(ammo.Velocity);
            var (exhaustLength, flame) = PlumeLook(MathF.Max(steering, boost), bodyLength, ammo.Color);

            ShapeSprites.DrawMissile(cx, cy, angle,
                bodyLength, ammo.Radius,
                exhaustLength, ammo.Radius * ExhaustWidthScale,
                MissileBodyColor, flame);

            if (ShowPlumeAnchors) DrawPlumeAnchor(cx, cy, angle, bodyLength, exhaustLength);
        }
    }

    // What the round's effort level (0 = cruising, 1 = boosting or turning hard) makes the plume look like:
    // longer, whiter, brighter. The flame is the identity channel — the round's own ammo colour, Orange for
    // the player's racks and Red for an enemy's — and the work the round is doing washes it toward white, so
    // the player reads "this round is committed that way" off the same numbers the sim computed. Shared by
    // the live pass and the diagnostic bench, so the bench cannot flatter the real draw.
    private static (float Length, Color Flame) PlumeLook(float effort, float bodyLength, AmmoColor roundColour)
    {
        float exhaustLength = bodyLength * (MissileExhaustCruise + (MissileExhaustHardTurn - MissileExhaustCruise) * effort);
        var flame = MixTowardWhite(AmmoColorOf(roundColour), ExhaustWhiteMix + ExhaustHardTurnMix * effort);
        flame.A = (byte)(ExhaustCruiseAlpha + (255 - ExhaustCruiseAlpha) * effort);
        return (exhaustLength, flame);
    }

    // Diagnostic: the plume anchor the code computes, and the heading line it sits on. The cross is where
    // the plume's centre is *meant* to be, and it comes from the same helper the draw calls — so when the
    // drawn flame is not centred on it, the placement is wrong rather than the eye.
    private static void DrawPlumeAnchor(float cx, float cy, float angleRad, float bodyLength, float exhaustLength)
    {
        var anchor = ShapeSprites.PlumeAnchor(cx, cy, angleRad, bodyLength, exhaustLength);
        // The same heading convention ShapeSprites rotates the sprites by: an angle of 0 faces up the screen.
        float forwardX = MathF.Sin(angleRad), forwardY = -MathF.Cos(angleRad);
        float reach = bodyLength * 0.5f + exhaustLength;
        Raylib.DrawLine((int)(cx - forwardX * reach), (int)(cy - forwardY * reach),
            (int)(cx + forwardX * bodyLength * 0.5f), (int)(cy + forwardY * bodyLength * 0.5f), AnchorHeadingLine);
        Raylib.DrawLine((int)anchor.X - AnchorCrossReach, (int)anchor.Y, (int)anchor.X + AnchorCrossReach, (int)anchor.Y, AnchorCross);
        Raylib.DrawLine((int)anchor.X, (int)anchor.Y - AnchorCrossReach, (int)anchor.X, (int)anchor.Y + AnchorCrossReach, AnchorCross);
    }

    // Diagnostic bench (V/B keys): the same draw call repeated over every heading and both plume sizes, at
    // a fixed screen position, so one screenshot measures the plume in all sixteen orientations at once and
    // nothing else in the frame is orange or grey.
    private static void DrawMissileBench()
    {
        for (int row = 0; row < BenchRows; row++)
        {
            float effort = row == 0 ? 0f : 1f;
            var (exhaustLength, flame) = PlumeLook(effort, BenchRadius * MissileBodyStretch, AmmoColor.Orange);

            for (int column = 0; column < BenchHeadings.Length; column++)
            {
                float angle = BenchHeadings[column] * MathF.PI / 180f;
                float cx = BenchFirstColumn + column * BenchColumnGap;
                float cy = BenchFirstRow + row * BenchRowGap;

                ShapeSprites.DrawMissile(cx, cy, angle,
                    BenchRadius * MissileBodyStretch, BenchRadius,
                    exhaustLength, BenchRadius * ExhaustWidthScale,
                    MissileBodyColor, flame);

                if (ShowPlumeAnchors) DrawPlumeAnchor(cx, cy, angle, BenchRadius * MissileBodyStretch, exhaustLength);
            }
        }
    }

    // Diagnostic switches, set by the B and V diagnostic keys. Render-only: nothing in the sim reads them.
    public static bool ShowPlumeAnchors { get; set; }
    public static bool ShowMissileBench { get; set; }

    const float BenchRadius = 6f;
    const int BenchFirstColumn = 150, BenchColumnGap = 220, BenchFirstRow = 200, BenchRowGap = 450, BenchRows = 2;
    static readonly float[] BenchHeadings = [0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f];

    static readonly Color AnchorHeadingLine = new(120, 200, 230, 200);
    static readonly Color AnchorCross = new(255, 255, 255, 255);
    const int AnchorCrossReach = 7;

    // A guided round's body is painted hardware, not a faction marker: light grey for both sides, so a
    // missile reads as an object against the dark rather than as a coloured dot. The flame is the colour
    // channel — the round's ammo colour — and the elongated shape is what says "guided".
    static readonly Color MissileBodyColor = new(205, 208, 218, 255);

    // Drawn body length per px of collision radius. Three times what it was: at 2.5 a 4 px round drew a
    // 10 px dart, which at the ranges a missile is fired across read as a bright dot rather than an object
    // with a nose and a tail.
    const float MissileBodyStretch = 7.5f;
    const float MissileExhaustCruise = 0.45f, MissileExhaustHardTurn = 1f;
    const float ExhaustWidthScale = 1.5f;
    // The flame keeps its full colour while cruising and only washes toward white at a hard turn, so the
    // saturation reads as "this is an orange plume" at rest and as heat when the round is committed. With a
    // grey body the plume is the only colour on the round, so it is not washed at cruise any more.
    const float ExhaustWhiteMix = 0f, ExhaustHardTurnMix = 0.35f;
    // Cruise alpha stays high: a guided round the player cannot see is a guided round the player
    // cannot dodge, so the flame dims with the turn but never goes faint.
    const byte ExhaustCruiseAlpha = 210;

    static Color MixTowardWhite(Color color, float amount) => new(
        (byte)(color.R + (255 - color.R) * amount),
        (byte)(color.G + (255 - color.G) * amount),
        (byte)(color.B + (255 - color.B) * amount),
        color.A);

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
            bool mounted = em.TryGetComponent<Turret>(entity, out var turret);
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

            // A guided mount is a rack: it never swings to face anything, so its marker is a square in
            // the hull frame instead of the aim-following point.
            if (turret.Weapon.Homing)
                RenderHelpers.DrawRackMarker(cx, cy, radius, Angle.ToDeg(hullRotation), Palette.TurretMounted);
            else
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

        // The player's collider, in the same cyan as every other hull's. It is still one disc: giving a
        // player hull a shape is a balance decision that has not been made (PLAN.md), so the honest
        // diagnostic is a disc rather than an absence of one.
        if (diagnostics)
            ShipSpriteRenderer.DrawHullShape(HullShape.Circle, shipType.Radius, screenCx, screenCy, angleDeg);
    }
}
