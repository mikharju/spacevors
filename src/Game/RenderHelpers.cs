using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Stats;

namespace Spacevors.Game;

internal static class RenderHelpers
{
    // World → screen position. The camera target is the centre of the window; 1 world unit = 1 px.
    public static (float X, float Y) ToScreen(Vector2 world, float camX, float camY, int windowWidth, int windowHeight)
    {
        return (world.X - camX + windowWidth / 2f, world.Y - camY + windowHeight / 2f);
    }

    public static bool IsOffScreen(float cx, float cy, float extent, int windowWidth, int windowHeight)
    {
        return cx < -extent || cx > windowWidth + extent || cy < -extent || cy > windowHeight + extent;
    }

    // Farthest corner distance of a box drawn centered on its middle.
    public static float HalfDiagonal(float width, float height)
    {
        return 0.5f * MathF.Sqrt(width * width + height * height);
    }

    public static Rectangle FullSource(Texture2D tex) => new(0f, 0f, tex.Width, tex.Height);

    public static System.Numerics.Vector2 CenterOrigin(Rectangle rect) => new(rect.Width / 2f, rect.Height / 2f);

    // An enemy ship's turret: a plain dot on the hull, which has no mounts of its own.
    public const float TurretDotSize = 8f;

    public static void DrawTurretDot(float cx, float cy, Color color)
        => Raylib.DrawRectangle((int)(cx - TurretDotSize / 2f), (int)(cy - TurretDotSize / 2f), (int)TurretDotSize, (int)TurretDotSize, color);

    // Turret marker: how big a hull mount reads in world pixels.
    public static float TurretMarkerRadius(TurretSize size) => size == TurretSize.Large ? 9f : 6f;

    // How far a mounted marker reaches from its centre, for off-screen culling.
    public static float TurretMarkerExtent(TurretSize size) => TurretMarkerRadius(size) * MarkerApexReach;

    // A mounted turret: the mount circle with a point sticking out of it toward where the gun aims.
    // The triangle's base corners sit at 0.93 radius — inside the opaque circle, which is a disc, so
    // the whole base is hidden and only the tip at 1.9 radius shows.
    // Vertex order must be clockwise in screen space; counter-clockwise triangles are culled.
    public static void DrawTurretMarker(float cx, float cy, float radius, float angleDeg, Color color)
    {
        Raylib.DrawCircle((int)cx, (int)cy, radius, color);

        float angle = angleDeg * MathF.PI / 180f;
        Raylib.DrawTriangle(
            Rotated(cx, cy, 0f, -radius * MarkerApexReach, angle),
            Rotated(cx, cy, -radius * MarkerTailHalfWidth, radius * MarkerTailInset, angle),
            Rotated(cx, cy, radius * MarkerTailHalfWidth, radius * MarkerTailInset, angle),
            color);
    }

    const float MarkerApexReach = 1.9f;
    const float MarkerTailHalfWidth = 0.9f;
    const float MarkerTailInset = 0.25f;

    // Ship salvage (a reroll charge): a short capsule — a rotated body, a cap at each end, a bright core
    // along the axis. Three batched draws, and shared by the world (a capsule tumbling where it fell,
    // drawn at its own simulated spin) and the HUD (the same shape, held still, beside the count) so the
    // pickup and what it buys are visibly the same object.
    //
    // The three colours are arguments because the field capsules share the silhouette and differ only in
    // colour and motion: steady grey buys a reroll, grey with a pulsing ring sweeps the field, a flashing
    // blue-grey-white capsule jams the fleet.
    public static void DrawSalvageCapsule(float cx, float cy, float radius, float spin, Color body, Color cap, Color core)
    {
        float halfLength = radius;
        float halfThickness = radius * CapsuleThicknessFraction;
        var axis = new System.Numerics.Vector2(MathF.Cos(spin), MathF.Sin(spin));
        var bodyRect = new Rectangle(cx, cy, halfLength * 2f, halfThickness * 2f);

        Raylib.DrawRectanglePro(bodyRect, CenterOrigin(bodyRect), Angle.ToDeg(spin), body);

        ShapeSprites.DrawCircle(cx + axis.X * halfLength, cy + axis.Y * halfLength, halfThickness, cap);
        ShapeSprites.DrawCircle(cx - axis.X * halfLength, cy - axis.Y * halfLength, halfThickness, cap);

        float coreLength = halfLength * CapsuleCoreFraction;
        ShapeSprites.DrawStreak(
            new System.Numerics.Vector2(cx - axis.X * coreLength, cy - axis.Y * coreLength),
            new System.Numerics.Vector2(cx + axis.X * coreLength, cy + axis.Y * coreLength),
            halfThickness * 1.2f,
            core);
    }

    public static void DrawSalvageCapsule(float cx, float cy, float radius, float spin) =>
        DrawSalvageCapsule(cx, cy, radius, spin, Palette.SalvageBody, Palette.SalvageCap, Palette.SalvageCore);

    // The ring on a salvage-field capsule: the field it is about to release, drawn on the capsule itself so
    // the shape that says "salvage" also says "this one acts on everything lying out there". The pulse runs
    // off the capsule's own simulated spin, so — like the capsule's pose — a seeded run draws it twice.
    public static void DrawFieldRing(float cx, float cy, float radius, float spin)
    {
        float pulse = 0.5f + 0.5f * MathF.Cos(spin * FieldRingPulsePerSpin);
        float outer = radius * (1f + FieldRingGrowFraction * pulse);
        float thickness = MathF.Max(MinFieldRingThickness, radius * FieldRingThicknessFraction);
        var color = Ui.WithAlpha(Palette.SalvageFieldRing, FieldRingAlphaMin + FieldRingAlphaRange * pulse);

        Raylib.DrawRing(new System.Numerics.Vector2(cx, cy), outer - thickness, outer, 0f, 0f, FieldRingSegments, color);
    }

    const int FieldRingSegments = 24;

    const float FieldRingPulsePerSpin = 2f;
    const float FieldRingGrowFraction = 0.35f;
    const float FieldRingThicknessFraction = 0.12f;
    const float MinFieldRingThickness = 1.5f;
    const float FieldRingAlphaMin = 0.25f;
    const float FieldRingAlphaRange = 0.75f;

    const float CapsuleThicknessFraction = 0.38f;
    const float CapsuleCoreFraction = 0.55f;

    // How wide a salvage capsule reads, so the HUD icon and the world pickup are the same size.
    public static float SalvageIconRadius(float scale) => 7f * scale;

    // A missile rack: a square riding in the hull frame instead of a point toward the target. A guided
    // round picks its own bearing once it is away from the mount, so a marker that swung to aim would
    // claim the mount is steering something it does not. The arc sector is still drawn underneath:
    // what the rack may cover is still a property of the mount.
    public static void DrawRackMarker(float cx, float cy, float radius, float hullAngleDeg, Color color)
    {
        // DrawRectanglePro centres on rec.xy when the origin is the rect's half-size, so anchoring the
        // rect at (cx - half) drew the square half a side up-left of the mount (probe: the placement
        // trap TROUBLE_SHOOTING #59 records for DrawTexturePro, same family).
        float half = radius * RackMarkerHalfSide;
        var square = new Rectangle(cx, cy, half * 2f, half * 2f);
        Raylib.DrawRectanglePro(square, CenterOrigin(square), hullAngleDeg, color);
    }

    const float RackMarkerHalfSide = 0.8f;

    static System.Numerics.Vector2 Rotated(float cx, float cy, float x, float y, float angle)
        => new(cx + x * MathF.Cos(angle) - y * MathF.Sin(angle),
               cy + x * MathF.Sin(angle) + y * MathF.Cos(angle));
}
