using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Game;

public static class EnemyShipRenderer
{
    // Every hostile hull reads crimson, whatever art it wears: the faction is one value here, so an
    // elite or boss hue later is a second value, not a per-hull table.
    const HullFaction Faction = HullFaction.Enemy;

    static readonly Color EnemyTurretColor = new(255, 140, 30, 255);

    // The wind-up tell a Burst hull shows before its volley (plans/ENEMY_BEHAVIORS.md B7): a charge that
    // grows over the last seconds of the hull's cycle. Warm rather than a beam colour, so it cannot be
    // misread as incoming fire — what it promises is the wall of slugs a moment later.
    static readonly Color BurstTellColor = new(255, 214, 96, 255);
    const float BurstTellBaseRadius = 0.6f;
    const float BurstTellGrowRadius = 0.9f;
    const int BurstTellBaseAlpha = 70;
    const int BurstTellGrowAlpha = 160;

    private static readonly Dictionary<LitBlock, List<(Rectangle Dest, float AngleDeg, float Damage)>> _litDraws = new();
    private static readonly List<(float Cx, float Cy)> _turretMarkers = new();
    private static readonly List<(float Cx, float Cy, float Radius, float Charge)> _burstTells = new();
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
        _burstTells.Clear();
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
                    LitGroupRenderer.Add(_litDraws, lit, Faction, dest, angleDeg, damage);
                else
                    // Flat sprites draw straight away while lit ones wait for the batch flush below,
                    // so a mixed layer draws flat variants under lit ones.
                    Raylib.DrawTexturePro(t, RenderHelpers.FullSource(t), dest, RenderHelpers.CenterOrigin(dest), angleDeg, Palette.HullFallbackTint(Faction));
            }
            else
            {
                ShipSpriteRenderer.DrawFallbackShip(screenCx, screenCy, enemyShip.Radius, angleDeg, Faction);
            }

            _turretMarkers.Add((screenCx, screenCy));

            float charge = BurstCharge(em, enemyShip);
            if (charge > 0f) _burstTells.Add((screenCx, screenCy, enemyShip.Radius, charge));

            if (diagnostics)
                _diagnosticShapes.Add((screenCx, screenCy, angleDeg, enemyShip.GraphicsId, enemyShip.Radius));
        }

        // Sub-segmented like the asteroid pass: the lit blocks and the collider overlay have very
        // different costs, and the spike row names whichever one stalled.
        RenderTimings.SubPass("enemyShips.litDraws", () => LitGroupRenderer.Draw(_litDraws));

        foreach (var (cx, cy) in _turretMarkers)
            RenderHelpers.DrawTurretDot(cx, cy, EnemyTurretColor);

        // Over the hull, after the batch flush, so the charge reads as the hull charging and not as a
        // halo sitting behind it.
        RenderTimings.SubPass("enemyShips.burstTell", () =>
        {
            foreach (var (cx, cy, radius, charge) in _burstTells) DrawBurstTell(cx, cy, radius, charge);
        });

        // After the batch flush, so the collider sits over the sprite it belongs to.
        if (diagnostics)
            RenderTimings.SubPass("enemyShips.overlay", () =>
            {
                foreach (var (cx, cy, angleDeg, graphicsId, radius) in _diagnosticShapes)
                    ShipSpriteRenderer.DrawHullShape(HullShapes.ForEnemy(graphicsId), radius, cx, cy, angleDeg);
            });
    }

    private static void DrawEnemyShipsFallback(EntityManager em, float camX, float camY, int windowWidth, int windowHeight, bool diagnostics)
    {
        foreach (var (entity, enemyShip) in em.GetEntitiesWithComponents<EnemyShip>())
        {
            var shipPos = em.GetComponent<Position>(entity);
            var shipRot = em.GetComponent<Rotation>(entity);

            var (cx, cy) = RenderHelpers.ToScreen(shipPos.Value, camX, camY, windowWidth, windowHeight);

            if (RenderHelpers.IsOffScreen(cx, cy, enemyShip.Radius, windowWidth, windowHeight)) continue;

            ShipSpriteRenderer.DrawFallbackShip(cx, cy, enemyShip.Radius, shipRot.Angle * 180f / MathF.PI, Faction);

            RenderHelpers.DrawTurretDot(cx, cy, EnemyTurretColor);

            float charge = BurstCharge(em, enemyShip);
            if (charge > 0f) DrawBurstTell(cx, cy, enemyShip.Radius, charge);

            if (diagnostics)
                ShipSpriteRenderer.DrawHullShape(HullShapes.ForEnemy(enemyShip.GraphicsId), enemyShip.Radius, cx, cy, shipRot.Angle * 180f / MathF.PI);
        }
    }

    // How far through its wind-up a Burst hull is: 0 for every other hull and outside the tell window,
    // so one value drives both whether anything is drawn and how far it has charged.
    private static float BurstCharge(EntityManager em, EnemyShip enemyShip) =>
        enemyShip.Behavior == EnemyBehavior.Burst ? EnemyShipStats.BurstTell(enemyShip.HoldBearing, em.ElapsedTime) : 0f;

    private static void DrawBurstTell(float cx, float cy, float hullRadius, float charge)
    {
        float radius = hullRadius * (BurstTellBaseRadius + BurstTellGrowRadius * charge);
        var color = new Color(BurstTellColor.R, BurstTellColor.G, BurstTellColor.B, (byte)(BurstTellBaseAlpha + BurstTellGrowAlpha * charge));
        ShapeSprites.DrawGlow(cx, cy, radius, color);
    }
}
