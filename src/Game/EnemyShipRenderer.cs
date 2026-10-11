using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Game;

public static class EnemyShipRenderer
{
    const float TurretSize = 8f;

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
                    // Flat sprites draw straight away while lit ones wait for the batch flush below,
                    // so a mixed layer draws flat variants under lit ones.
                    Raylib.DrawTexturePro(t, RenderHelpers.FullSource(t), dest, RenderHelpers.CenterOrigin(dest), angleDeg, Color.White);
            }
            else
            {
                ShipSpriteRenderer.DrawFallbackShip(screenCx, screenCy, enemyShip.Radius, angleDeg);
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

            ShipSpriteRenderer.DrawFallbackShip(cx, cy, enemyShip.Radius, shipRot.Angle * 180f / MathF.PI);

            Raylib.DrawRectangle(
                (int)(cx - TurretSize / 2f),
                (int)(cy - TurretSize / 2f),
                (int)TurretSize,
                (int)TurretSize,
                new Color(255, 140, 30, 255)
            );

            if (diagnostics) Raylib.DrawCircle((int)cx, (int)cy, enemyShip.Radius, new Color(255, 165, 0, 60));
        }
    }
}
