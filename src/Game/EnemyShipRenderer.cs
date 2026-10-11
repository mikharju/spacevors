using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Game;

public static class EnemyShipRenderer
{
    const float TurretSize = 8f;

    private static readonly Color FallbackShipColor = new(180, 60, 60, 255);
    private static readonly Dictionary<LitSprite, List<(Rectangle Dest, float AngleDeg)>> _litDraws = new();
    private static readonly List<(int X, int Y)> _turretRects = new();
    private static readonly List<(int X, int Y, int Radius)> _diagnosticCircles = new();

    public static void Draw(EntityManager em, float camX, float camY, int windowWidth, int windowHeight, bool diagnostics)
    {
        LitGroupRenderer.Clear(_litDraws);
        _turretRects.Clear();
        _diagnosticCircles.Clear();

        if (ImageLoader.EnemyShipTextures == null)
        {
            DrawEnemyShipsFallback(em, camX, camY, windowWidth, windowHeight, diagnostics);
            return;
        }

        foreach (var (entity, enemyShip) in em.GetEntitiesWithComponents<EnemyShip>())
        {
            var shipPos = em.GetComponent<Position>(entity);
            var shipRot = em.GetComponent<Rotation>(entity);

            var (screenCx, screenCy) = RenderHelpers.ToScreen(shipPos.Value, camX, camY, windowWidth, windowHeight);
            float angleDeg = shipRot.Angle * 180f / MathF.PI;

            string? texKey = EnemyShipType.FromGraphicsId(enemyShip.GraphicsId)?.TextureKey;

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
                var dest = new Rectangle(screenCx, screenCy, t.Width * scale, t.Height * scale);

                if (lit != null)
                    LitGroupRenderer.Add(_litDraws, lit, dest, angleDeg);
                else
                    Raylib.DrawTexturePro(t, RenderHelpers.FullSource(t), dest, RenderHelpers.CenterOrigin(dest), angleDeg, Color.White);
            }
            else
            {
                DrawEnemyShipFallback(shipPos.Value, shipRot.Angle, enemyShip.Radius, camX, windowWidth, camY, windowHeight, diagnostics);
            }

            _turretRects.Add(((int)(screenCx - TurretSize / 2f), (int)(screenCy - TurretSize / 2f)));

            if (diagnostics) _diagnosticCircles.Add(((int)screenCx, (int)screenCy, (int)enemyShip.Radius));
        }

        LitGroupRenderer.Draw(_litDraws);

        foreach (var (x, y) in _turretRects)
            Raylib.DrawRectangle(x, y, (int)TurretSize, (int)TurretSize, new Color(255, 140, 30, 255));

        if (diagnostics)
            foreach (var (x, y, radius) in _diagnosticCircles)
                Raylib.DrawCircle(x, y, radius, new Color(255, 165, 0, 60));
    }

    private static void DrawEnemyShipsFallback(EntityManager em, float camX, float camY, int windowWidth, int windowHeight, bool diagnostics)
    {
        foreach (var (entity, enemyShip) in em.GetEntitiesWithComponents<EnemyShip>())
        {
            var shipPos = em.GetComponent<Position>(entity);
            var shipRot = em.GetComponent<Rotation>(entity);

            var (cx, cy) = RenderHelpers.ToScreen(shipPos.Value, camX, camY, windowWidth, windowHeight);

            if (RenderHelpers.IsOffScreen(cx, cy, enemyShip.Radius, windowWidth, windowHeight)) continue;

            float angle = shipRot.Angle;
            float size = enemyShip.Radius;

            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);

            var tipLocal = new Vector2(0f, size);
            var tip = new Vector2(tipLocal.X * cos - tipLocal.Y * sin, tipLocal.X * sin + tipLocal.Y * cos);

            var leftLocal = new Vector2(-size * 0.4f, -size * 0.6f);
            var left = new Vector2(leftLocal.X * cos - leftLocal.Y * sin, leftLocal.X * sin + leftLocal.Y * cos);

            var rightLocal = new Vector2(size * 0.4f, -size * 0.6f);
            var right = new Vector2(rightLocal.X * cos - rightLocal.Y * sin, rightLocal.X * sin + rightLocal.Y * cos);

            var (tipX, tipY) = RenderHelpers.ToScreen(shipPos.Value + tip, camX, camY, windowWidth, windowHeight);
            var (rightX, rightY) = RenderHelpers.ToScreen(shipPos.Value + right, camX, camY, windowWidth, windowHeight);
            var (leftX, leftY) = RenderHelpers.ToScreen(shipPos.Value + left, camX, camY, windowWidth, windowHeight);

            Raylib.DrawTriangle(
                new System.Numerics.Vector2(tipX, tipY),
                new System.Numerics.Vector2(rightX, rightY),
                new System.Numerics.Vector2(leftX, leftY),
                FallbackShipColor
            );

            Raylib.DrawRectangle(
                (int)(cx - TurretSize / 2f),
                (int)(cy - TurretSize / 2f),
                (int)TurretSize,
                (int)TurretSize,
                new Color(255, 140, 30, 255)
            );

            if (diagnostics) Raylib.DrawCircle((int)cx, (int)cy, (int)enemyShip.Radius, new Color(255, 165, 0, 60));
        }
    }

    private static void DrawEnemyShipFallback(Vector2 pos, float angle, float size, float camX, int windowWidth, float camY, int windowHeight, bool diagnostics)
    {
        float cos = (float)Math.Cos(angle);
        float sin = (float)Math.Sin(angle);

        var tipLocal = new Vector2(0f, size);
        var tip = new Vector2(tipLocal.X * cos - tipLocal.Y * sin, tipLocal.X * sin + tipLocal.Y * cos);

        var leftLocal = new Vector2(-size * 0.4f, -size * 0.6f);
        var left = new Vector2(leftLocal.X * cos - leftLocal.Y * sin, leftLocal.X * sin + leftLocal.Y * cos);

        var rightLocal = new Vector2(size * 0.4f, -size * 0.6f);
        var right = new Vector2(rightLocal.X * cos - rightLocal.Y * sin, rightLocal.X * sin + rightLocal.Y * cos);

        var (tipX, tipY) = RenderHelpers.ToScreen(pos + tip, camX, camY, windowWidth, windowHeight);
        var (rightX, rightY) = RenderHelpers.ToScreen(pos + right, camX, camY, windowWidth, windowHeight);
        var (leftX, leftY) = RenderHelpers.ToScreen(pos + left, camX, camY, windowWidth, windowHeight);

        Raylib.DrawTriangle(
            new System.Numerics.Vector2(tipX, tipY),
            new System.Numerics.Vector2(rightX, rightY),
            new System.Numerics.Vector2(leftX, leftY),
            Color.Red
        );

        if (diagnostics)
        {
            var (cx, cy) = RenderHelpers.ToScreen(pos, camX, camY, windowWidth, windowHeight);
            Raylib.DrawCircle((int)cx, (int)cy, (int)size, new Color(255, 165, 0, 60));
        }
    }
}
