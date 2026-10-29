using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Game;

public static class EnemyShipRenderer
{
    static readonly Color EnemyTurretColor = new(255, 140, 30, 255);

    private static readonly Dictionary<LitSprite, List<(Rectangle Dest, float AngleDeg, float Damage)>> _litDraws = new();
    private static readonly List<(float Cx, float Cy)> _turretMarkers = new();
    private static readonly List<(float Cx, float Cy, float AngleDeg, byte GraphicsId, float Radius)> _diagnosticShapes = new();

    // Sprite of one GraphicsId, resolved once per frame. Every enemy of a type shares it, so the
    // texture-key lookups run per type instead of per entity.
    private static readonly Dictionary<byte, (LitSprite? Lit, Texture2D? Flat)> _spritesByGraphicsId = new();

    private static (LitSprite? Lit, Texture2D? Flat) ResolveSprite(byte graphicsId)
    {
        if (_spritesByGraphicsId.TryGetValue(graphicsId, out var cached)) return cached;

        LitSprite? lit = null;
        Texture2D? flat = null;
        string? texKey = EnemyShipType.FromGraphicsId(graphicsId)?.TextureKey;
        if (texKey != null && ImageLoader.EnemyShipLitSprites != null && ImageLoader.EnemyShipLitSprites.TryGetValue(texKey, out var litCandidate))
            lit = litCandidate;
        else if (texKey != null && ImageLoader.EnemyShipTextures!.TryGetValue(texKey, out var flatCandidate) && flatCandidate.Id != 0)
            flat = flatCandidate;

        var resolved = (lit, flat);
        _spritesByGraphicsId[graphicsId] = resolved;
        return resolved;
    }

    public static void Draw(EntityManager em, float camX, float camY, int windowWidth, int windowHeight, bool diagnostics)
    {
        LitGroupRenderer.Clear(_litDraws);
        _turretMarkers.Clear();
        _diagnosticShapes.Clear();
        _spritesByGraphicsId.Clear();

        if (ImageLoader.EnemyShipTextures == null)
        {
            DrawEnemyShipsFallback(em, camX, camY, windowWidth, windowHeight, diagnostics);
            return;
        }

        foreach (var (entity, enemyShip, shipPos, shipRot) in em.GetEntitiesWithComponents<EnemyShip, Position, Rotation>())
        {
            var (screenCx, screenCy) = RenderHelpers.ToScreen(shipPos.Value, camX, camY, windowWidth, windowHeight);
            float angleDeg = shipRot.Angle * 180f / MathF.PI;

            var (lit, flat) = ResolveSprite(enemyShip.GraphicsId);
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

                float damage = em.TryGetComponent<Health>(entity, out var health) ? DamageShading.FromHealth(health) : 0f;

                if (lit != null)
                    LitGroupRenderer.Add(_litDraws, lit, dest, angleDeg, damage);
                else
                    // Flat sprites draw straight away while lit ones wait for the batch flush below,
                    // so a mixed layer draws flat variants under lit ones.
                    Raylib.DrawTexturePro(t, RenderHelpers.FullSource(t), dest, RenderHelpers.CenterOrigin(dest), angleDeg, Color.White);
            }
            else
            {
                ShipSpriteRenderer.DrawFallbackShip(screenCx, screenCy, enemyShip.Radius, angleDeg);
            }

            _turretMarkers.Add((screenCx, screenCy));

            if (diagnostics)
                _diagnosticShapes.Add((screenCx, screenCy, angleDeg, enemyShip.GraphicsId, enemyShip.Radius));
        }

        LitGroupRenderer.Draw(_litDraws);

        foreach (var (cx, cy) in _turretMarkers)
            RenderHelpers.DrawTurretDot(cx, cy, EnemyTurretColor);

        // After the batch flush, so the collider sits over the sprite it belongs to.
        if (diagnostics)
            foreach (var (cx, cy, angleDeg, graphicsId, radius) in _diagnosticShapes)
                ShipSpriteRenderer.DrawHullShape(HullShapes.ForEnemy(graphicsId), radius, cx, cy, angleDeg);
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

            RenderHelpers.DrawTurretDot(cx, cy, EnemyTurretColor);

            if (diagnostics)
                ShipSpriteRenderer.DrawHullShape(HullShapes.ForEnemy(enemyShip.GraphicsId), enemyShip.Radius, cx, cy, shipRot.Angle * 180f / MathF.PI);
        }
    }
}
