using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;

namespace Spacevors.Game;

public static class WorldRenderer
{
    public static void Draw(
        EntityManager em, Entity playerEntity, ShipType shipType, bool diagnostics,
        float camX, float camY, int windowWidth, int windowHeight)
    {
        DrawAsteroids(em, camX, camY, windowWidth, windowHeight, diagnostics);
        DrawAmmo(em, camX, camY, windowWidth, windowHeight);
        DrawExplosions(em, camX, camY, windowWidth, windowHeight);
        DrawSparks(em, camX, camY, windowWidth, windowHeight);
        DrawGreenSparks(em, camX, camY, windowWidth, windowHeight);
        ThrusterFlameRenderer.Draw(em, camX, camY, windowWidth, windowHeight);
        DrawPlayerShip(em, playerEntity, camX, camY, windowWidth, windowHeight, shipType, diagnostics);
        EnemyShipRenderer.Draw(em, camX, camY, windowWidth, windowHeight, diagnostics);
        DrawMines(em, camX, camY, windowWidth, windowHeight);
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
        foreach (var (entity, asteroid, rot) in em.GetEntitiesWithComponents<Asteroid, Rotation>())
        {
            var pos = em.GetComponent<Position>(entity);
            float cx = (float)pos.Value.X - camX + windowWidth / 2f;
            float cy = (float)pos.Value.Y - camY + windowHeight / 2f;

            float angleDeg = rot.Angle * 180f / MathF.PI;
            AsteroidSprite? sprite = GetAsteroidSprite(asteroid);
            Texture2D? baseTex = sprite?.Lit?.Base ?? sprite?.Flat;

            float extent = baseTex.HasValue && baseTex.Value.Id != 0
                ? RenderHelpers.HalfDiagonal(asteroid.Radius * 2f, asteroid.Radius * 2f * (float)baseTex.Value.Height / baseTex.Value.Width)
                : RenderHelpers.HalfDiagonal(asteroid.Radius * 2f, asteroid.Radius * 2f);

            if (RenderHelpers.IsOffScreen(cx, cy, extent, windowWidth, windowHeight)) continue;

            if (diagnostics) Raylib.DrawCircle((int)cx, (int)cy, (int)asteroid.Radius, new Color(255, 0, 0, 60));

            if (baseTex.HasValue && baseTex.Value.Id != 0)
                DrawAsteroidSprite(sprite, baseTex.Value, cx, cy, asteroid.Radius * 2f, angleDeg);
            else
                Raylib.DrawRectanglePro(
                    new Rectangle((int)cx - (int)asteroid.Radius, (int)cy - (int)asteroid.Radius, (int)(asteroid.Radius * 2), (int)(asteroid.Radius * 2)),
                    new System.Numerics.Vector2(asteroid.Radius, asteroid.Radius),
                    angleDeg,
                    new Color(200, 200, 210, 255)
                );
        }
    }

    private static AsteroidSprite? GetAsteroidSprite(Asteroid asteroid)
    {
        var sprites = asteroid.IsSmall ? ImageLoader.AsteroidSmallSprites : ImageLoader.AsteroidLargeSprites;
        if (sprites == null || asteroid.Variant >= sprites.Length) return null;
        return sprites[asteroid.Variant];
    }

    // Lit when the variant has normal + depth maps, otherwise a flat texture.
    private static void DrawAsteroidSprite(AsteroidSprite? sprite, Texture2D baseTex, float cx, float cy, float diameter, float angleDeg)
    {
        float scale = diameter / baseTex.Width;
        var source = new Rectangle(0f, 0f, baseTex.Width, baseTex.Height);
        var dest = new Rectangle(cx, cy, baseTex.Width * scale, baseTex.Height * scale);
        var origin = new System.Numerics.Vector2(dest.Width / 2f, dest.Height / 2f);

        LitSprite? lit = sprite?.Lit;
        bool drawn = lit != null && Lighting.TryDraw(lit, source, dest, origin, angleDeg);
        if (!drawn)
            Raylib.DrawTexturePro(baseTex, source, dest, origin, angleDeg, Color.White);
    }

    private static void DrawAmmo(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, ammo) in em.GetEntitiesWithComponents<Ammo>())
        {
            var pos = em.GetComponent<Position>(entity);
            float cx = (float)pos.Value.X - camX + windowWidth / 2f;
            float cy = (float)pos.Value.Y - camY + windowHeight / 2f;

            if (RenderHelpers.IsOffScreen(cx, cy, ammo.Radius, windowWidth, windowHeight)) continue;

            Color color = ammo.Color switch
            {
                AmmoColor.Green => new Color(80, 255, 80, 255),
                AmmoColor.Blue => new Color(100, 180, 255, 255),
                AmmoColor.Red => new Color(255, 80, 80, 255),
                _ => new Color(255, 230, 100, 255)
            };

            Raylib.DrawCircle((int)cx, (int)cy, (int)ammo.Radius, color);
        }
    }

    private static void DrawExplosions(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, explosion) in em.GetEntitiesWithComponents<Explosion>())
        {
            var pos = em.GetComponent<Position>(entity);
            float cx = (float)pos.Value.X - camX + windowWidth / 2f;
            float cy = (float)pos.Value.Y - camY + windowHeight / 2f;

            float lifeRatio = explosion.Lifetime / explosion.InitialLifetime;
            float currentRadius = explosion.Radius * (1f + (1f - lifeRatio));

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

            Raylib.DrawCircle((int)cx, (int)cy, (int)Math.Max(currentRadius, 1f), color);
        }
    }

    private static void DrawSparks(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, spark) in em.GetEntitiesWithComponents<Spark>())
        {
            var pos = em.GetComponent<Position>(entity);
            float cx = (float)pos.Value.X - camX + windowWidth / 2f;
            float cy = (float)pos.Value.Y - camY + windowHeight / 2f;

            float lifeRatio = spark.Lifetime / spark.InitialLifetime;
            int size = (int)Math.Max(lifeRatio * 5f, 1f);

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
        foreach (var (entity, turret, rot) in em.GetEntitiesWithComponents<Turret, Rotation>())
        {
            if (turret.IsEnemy) continue;

            var pos = em.GetComponent<Position>(entity);
            float cx = (float)pos.Value.X - camX + windowWidth / 2f;
            float cy = (float)pos.Value.Y - camY + windowHeight / 2f;

            float turretSize = 8f;
            if (RenderHelpers.IsOffScreen(cx, cy, turretSize / 2f, windowWidth, windowHeight)) continue;

            Raylib.DrawRectangle(
                (int)(cx - turretSize / 2f),
                (int)(cy - turretSize / 2f),
                (int)turretSize,
                (int)turretSize,
                new Color(255, 180, 50, 255)
            );
        }
    }

    private static void DrawMines(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        Texture2D? mineTex = ImageLoader.MineTexture;
        bool hasTexture = mineTex.HasValue && mineTex.Value.Id != 0;

        foreach (var (entity, mine) in em.GetEntitiesWithComponents<EnemyMine>())
        {
            var pos = em.GetComponent<Position>(entity);
            float cx = (float)pos.Value.X - camX + windowWidth / 2f;
            float cy = (float)pos.Value.Y - camY + windowHeight / 2f;

            float extent = hasTexture
                ? RenderHelpers.HalfDiagonal(mine.Radius * 2f, mine.Radius * 2f * (float)mineTex!.Value.Height / mineTex.Value.Width)
                : mine.Radius;

            if (RenderHelpers.IsOffScreen(cx, cy, extent, windowWidth, windowHeight)) continue;

            bool hasHealth = em.HasComponent<Health>(entity);
            int healthAlpha = hasHealth && em.GetComponent<Health>(entity).Current >= 2 ? 180 : 255;

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

                Raylib.DrawCircle((int)cx, (int)cy, (int)(mine.Radius * 0.4f), new Color(255, 200, 200, healthAlpha));
            }
            else
            {
                // No-texture fallback: red disc plus a bright core so mines stay readable.
                if (hasHealth)
                    Raylib.DrawCircle((int)cx, (int)cy, (int)mine.Radius, new Color(255, 60, 60, healthAlpha));
                else
                    Raylib.DrawCircle((int)cx, (int)cy, (int)mine.Radius, new Color(255, 100, 100, 200));

                Raylib.DrawCircle((int)cx, (int)cy, (int)(mine.Radius * 0.4f), new Color(255, 200, 200, 255));
            }
        }
    }

    private static void DrawXpPickups(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, pickup) in em.GetEntitiesWithComponents<XpPickup>())
        {
            if (!em.HasComponent<Position>(entity)) continue;

            var pos = em.GetComponent<Position>(entity);
            float cx = (float)pos.Value.X - camX + windowWidth / 2f;
            float cy = (float)pos.Value.Y - camY + windowHeight / 2f;

            if (RenderHelpers.IsOffScreen(cx, cy, pickup.Radius, windowWidth, windowHeight)) continue;

            int alpha = pickup.Chased ? 255 : 180;
            Raylib.DrawCircle((int)cx, (int)cy, (int)pickup.Radius, new Color(50, 150, 255, alpha));
        }
    }

    private static void DrawHealthOrbs(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, orb) in em.GetEntitiesWithComponents<HealthOrb>())
        {
            if (!em.HasComponent<Position>(entity)) continue;

            var pos = em.GetComponent<Position>(entity);
            float cx = (float)pos.Value.X - camX + windowWidth / 2f;
            float cy = (float)pos.Value.Y - camY + windowHeight / 2f;

            if (RenderHelpers.IsOffScreen(cx, cy, orb.Radius, windowWidth, windowHeight)) continue;

            Raylib.DrawCircle((int)cx, (int)cy, (int)orb.Radius, new Color(50, 255, 100, 200));
        }
    }

    private static void DrawGreenSparks(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, spark) in em.GetEntitiesWithComponents<GreenSpark>())
        {
            if (!em.HasComponent<Position>(entity)) continue;

            var pos = em.GetComponent<Position>(entity);
            float cx = (float)pos.Value.X - camX + windowWidth / 2f;
            float cy = (float)pos.Value.Y - camY + windowHeight / 2f;

            float lifeRatio = spark.Lifetime / spark.InitialLifetime;
            int size = (int)Math.Max(lifeRatio * 5f, 1f);

            if (RenderHelpers.IsOffScreen(cx, cy, size, windowWidth, windowHeight)) continue;

            int alpha = (int)(lifeRatio * 255);

            Raylib.DrawCircle((int)cx, (int)cy, size, new Color(50, 255, 100, alpha));
        }
    }

    private static void DrawDebugMarkers(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, marker) in em.GetEntitiesWithComponents<DebugMarker>())
        {
            var pos = em.GetComponent<Position>(entity);
            float cx = (float)pos.Value.X - camX + windowWidth / 2f;
            float cy = (float)pos.Value.Y - camY + windowHeight / 2f;

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

        float screenCx = (float)shipPos.Value.X - camX + windowWidth / 2f;
        float screenCy = (float)shipPos.Value.Y - camY + windowHeight / 2f;
        float angleDeg = shipRot.Angle * 180f / MathF.PI;

        ShipSpriteRenderer.DrawShipSprite(shipType, screenCx, screenCy, shipType.Radius * 2f, angleDeg);

        if (diagnostics) Raylib.DrawCircle((int)screenCx, (int)screenCy, (int)shipType.Radius, new Color(0, 255, 0, 60));
    }
}
