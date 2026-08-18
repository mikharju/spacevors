using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;

namespace Spacevors.Game;

public static class EnemyShipRenderer
{
    public static void Draw(EntityManager em, float camX, float camY, int windowWidth, int windowHeight, bool diagnostics)
    {
        if (ImageLoader.EnemyShipTextures == null)
        {
            DrawEnemyShipsFallback(em, camX, camY, windowWidth, windowHeight, diagnostics);
            return;
        }

        foreach (var (entity, enemyShip) in em.GetEntitiesWithComponents<EnemyShip>())
        {
            var shipPos = em.GetComponent<Position>(entity);
            var shipRot = em.GetComponent<Rotation>(entity);

            float screenCx = (float)shipPos.Value.X - camX + windowWidth / 2f;
            float screenCy = (float)shipPos.Value.Y - camY + windowHeight / 2f;
            float angleDeg = shipRot.Angle * 180f / MathF.PI;

            string? texKey = enemyShip.GraphicsId switch
            {
                EnemyShipFactory.DefaultGraphicsId => "enemy-1",
                EnemyShipFactory.InterceptorGraphicsId => "interceptor",
                EnemyShipFactory.HeavyCannonGraphicsId => "heavy-cannon",
                _ => null
            };

            LitSprite? lit = null;
            Texture2D? flat = null;
            if (texKey != null && ImageLoader.EnemyShipLitSprites != null && ImageLoader.EnemyShipLitSprites.TryGetValue(texKey, out var litCandidate))
                lit = litCandidate;
            else if (texKey != null && ImageLoader.EnemyShipTextures.TryGetValue(texKey, out var flatCandidate) && flatCandidate.Id != 0)
                flat = flatCandidate;

            Texture2D? baseTex = lit != null ? lit.Base : flat;
            bool hasSprite = baseTex.HasValue && baseTex.Value.Id != 0;

            float extent = hasSprite
                ? RenderHelpers.HalfDiagonal(enemyShip.Radius * 2f, enemyShip.Radius * 2f * (float)baseTex!.Value.Height / baseTex.Value.Width)
                : enemyShip.Radius;

            if (RenderHelpers.IsOffScreen(screenCx, screenCy, extent, windowWidth, windowHeight)) continue;

            if (hasSprite)
            {
                var t = baseTex!.Value;
                float drawDiameter = enemyShip.Radius * 2f;
                float scale = drawDiameter / t.Width;
                float destWidth = t.Width * scale;
                float destHeight = t.Height * scale;

                bool drawn = lit != null && Lighting.TryDraw(
                    lit,
                    new Rectangle(0f, 0f, t.Width, t.Height),
                    new Rectangle(screenCx, screenCy, destWidth, destHeight),
                    new System.Numerics.Vector2(destWidth / 2f, destHeight / 2f),
                    angleDeg);

                if (!drawn)
                    Raylib.DrawTexturePro(
                        t,
                        new Rectangle(0f, 0f, t.Width, t.Height),
                        new Rectangle(screenCx, screenCy, destWidth, destHeight),
                        new System.Numerics.Vector2(destWidth / 2f, destHeight / 2f),
                        angleDeg,
                        Color.White);
            }
            else
            {
                DrawEnemyShipFallback(shipPos.Value, shipRot.Angle, enemyShip.Radius, camX, windowWidth, camY, windowHeight, diagnostics);
            }

            float cx = (float)shipPos.Value.X - camX + windowWidth / 2f;
            float cy = (float)shipPos.Value.Y - camY + windowHeight / 2f;
            float enemyTurretSize = 8f;
            Raylib.DrawRectangle(
                (int)(cx - enemyTurretSize / 2f),
                (int)(cy - enemyTurretSize / 2f),
                (int)enemyTurretSize,
                (int)enemyTurretSize,
                new Color(255, 140, 30, 255)
            );

            if (diagnostics) Raylib.DrawCircle((int)cx, (int)cy, (int)enemyShip.Radius, new Color(255, 165, 0, 60));
        }
    }

    private static void DrawEnemyShipsFallback(EntityManager em, float camX, float camY, int windowWidth, int windowHeight, bool diagnostics)
    {
        foreach (var (entity, enemyShip) in em.GetEntitiesWithComponents<EnemyShip>())
        {
            var shipPos = em.GetComponent<Position>(entity);
            var shipRot = em.GetComponent<Rotation>(entity);

            float cx = (float)shipPos.Value.X - camX + windowWidth / 2f;
            float cy = (float)shipPos.Value.Y - camY + windowHeight / 2f;

            if (RenderHelpers.IsOffScreen(cx, cy, enemyShip.Radius, windowWidth, windowHeight)) continue;

            float angle = shipRot.Angle;
            float size = enemyShip.Radius;

            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);

            var tipLocal = new System.Numerics.Vector2(0f, size);
            var tip = new System.Numerics.Vector2(tipLocal.X * cos - tipLocal.Y * sin, tipLocal.X * sin + tipLocal.Y * cos);

            var leftLocal = new System.Numerics.Vector2(-size * 0.4f, -size * 0.6f);
            var left = new System.Numerics.Vector2(leftLocal.X * cos - leftLocal.Y * sin, leftLocal.X * sin + leftLocal.Y * cos);

            var rightLocal = new System.Numerics.Vector2(size * 0.4f, -size * 0.6f);
            var right = new System.Numerics.Vector2(rightLocal.X * cos - rightLocal.Y * sin, rightLocal.X * sin + rightLocal.Y * cos);

            float tx1 = (float)shipPos.Value.X + tip.X - camX + windowWidth / 2f;
            float ty1 = (float)shipPos.Value.Y + tip.Y - camY + windowHeight / 2f;
            float tx2 = (float)shipPos.Value.X + right.X - camX + windowWidth / 2f;
            float ty2 = (float)shipPos.Value.Y + right.Y - camY + windowHeight / 2f;
            float tx3 = (float)shipPos.Value.X + left.X - camX + windowWidth / 2f;
            float ty3 = (float)shipPos.Value.Y + left.Y - camY + windowHeight / 2f;

            Color color;
            if (enemyShip.Damage > 1)
            {
                color = new Color(180, 60, 60, 255);
            }
            else if (enemyShip.Radius < 18f)
            {
                color = new Color(180, 80, 255, 255);
            }
            else
            {
                color = new Color(255, 80, 80, 255);
            }

            Raylib.DrawTriangle(
                new System.Numerics.Vector2(tx1, ty1),
                new System.Numerics.Vector2(tx2, ty2),
                new System.Numerics.Vector2(tx3, ty3),
                color
            );

            float enemyTurretSize = 8f;
            Raylib.DrawRectangle(
                (int)(cx - enemyTurretSize / 2f),
                (int)(cy - enemyTurretSize / 2f),
                (int)enemyTurretSize,
                (int)enemyTurretSize,
                new Color(255, 140, 30, 255)
            );

            if (diagnostics) Raylib.DrawCircle((int)cx, (int)cy, (int)enemyShip.Radius, new Color(255, 165, 0, 60));
        }
    }

    private static void DrawEnemyShipFallback(Vector2 pos, float angle, float size, float camX, int windowWidth, float camY, int windowHeight, bool diagnostics)
    {
        float cos = (float)Math.Cos(angle);
        float sin = (float)Math.Sin(angle);

        var tipLocal = new System.Numerics.Vector2(0f, size);
        var tip = new System.Numerics.Vector2(tipLocal.X * cos - tipLocal.Y * sin, tipLocal.X * sin + tipLocal.Y * cos);

        var leftLocal = new System.Numerics.Vector2(-size * 0.4f, -size * 0.6f);
        var left = new System.Numerics.Vector2(leftLocal.X * cos - leftLocal.Y * sin, leftLocal.X * sin + leftLocal.Y * cos);

        var rightLocal = new System.Numerics.Vector2(size * 0.4f, -size * 0.6f);
        var right = new System.Numerics.Vector2(rightLocal.X * cos - rightLocal.Y * sin, rightLocal.X * sin + rightLocal.Y * cos);

        float tx1 = (float)pos.X + tip.X - camX + windowWidth / 2f;
        float ty1 = (float)pos.Y + tip.Y - camY + windowHeight / 2f;
        float tx2 = (float)pos.X + right.X - camX + windowWidth / 2f;
        float ty2 = (float)pos.Y + right.Y - camY + windowHeight / 2f;
        float tx3 = (float)pos.X + left.X - camX + windowWidth / 2f;
        float ty3 = (float)pos.Y + left.Y - camY + windowHeight / 2f;

        Raylib.DrawTriangle(
            new System.Numerics.Vector2(tx1, ty1),
            new System.Numerics.Vector2(tx2, ty2),
            new System.Numerics.Vector2(tx3, ty3),
            Color.Red
        );

        if (diagnostics)
        {
            float cx = (float)pos.X - camX + windowWidth / 2f;
            float cy = (float)pos.Y - camY + windowHeight / 2f;
            Raylib.DrawCircle((int)cx, (int)cy, (int)size, new Color(255, 165, 0, 60));
        }
    }
}
