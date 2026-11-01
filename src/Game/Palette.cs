using Raylib_cs;

namespace Spacevors.Game;

// Colours used by more than one renderer. Everything else stays with the renderer that owns it.
internal static class Palette
{
    // Health bar endpoints: green at full hp, red when nearly dead.
    // HudRenderer switches between them, TargetingRenderer lerps across them.
    public static readonly Color HealthFull = new(80, 255, 80, 255);
    public static readonly Color HealthLow = new(255, 60, 60, 255);
    public static readonly Color HealthMid = new(255, 190, 80, 255);

    // Ship salvage — a capsule that buys a re-deal of the upgrade hand. Gunmetal greys, deliberately
    // unlit: every other pickup is a glowing orb. Shared by the tumbling capsule in the world and the
    // still icon beside the HUD count, so the pickup and what it buys read as one object.
    public static readonly Color SalvageBody = new(126, 132, 142, 235);
    public static readonly Color SalvageCap = new(178, 184, 194, 240);
    public static readonly Color SalvageCore = new(226, 231, 240, 200);

    // The salvage field — the same grey capsule with a slow pulsing ring (plans/
    // ECM_PULSE_AND_SALVAGE_FIELD.md). The capsule body says "salvage", the ring says "this one acts on
    // the field instead of buying something", and the ring's colour is the UI accent so it reads as a
    // mechanism rather than as another orb.
    public static readonly Color SalvageFieldRing = new(120, 200, 230, 150);

    // The ECM capsule: a blue-grey capsule flashing to white. Two levels of body and cap so the flash is a
    // lerp between them rather than an alpha fade — a charged object getting brighter, not a dim one
    // flickering. Distinct from the reroll capsule's steady grey and from the salvage field's ring.
    public static readonly Color EcmBodyDim = new(96, 110, 132, 235);
    public static readonly Color EcmBodyLit = new(188, 206, 234, 245);
    public static readonly Color EcmCapDim = new(146, 162, 186, 240);
    public static readonly Color EcmCapLit = new(238, 245, 255, 250);
    public static readonly Color EcmCore = new(225, 240, 255, 235);

    // Everything the jam itself looks like: the ring that fires out of a picked capsule, the arcs on a
    // jammed hull, and the edge marker pointing at a capsule waiting on the field.
    public static readonly Color EcmWave = new(120, 180, 255, 90);
    public static readonly Color EcmArc = new(170, 215, 255, 215);
    public static readonly Color EcmIndicator = new(160, 200, 250, 255);

    // Soft-depth UI language (plans/UI_POLISH.md): one accent used sparingly,
    // off-white text, translucent panels so stars faintly show through.
    public static readonly Color Accent = new(120, 200, 230, 255);
    public static readonly Color TextBright = new(230, 235, 245, 255);
    public static readonly Color TextMuted = new(150, 160, 175, 255);
    public static readonly Color PanelFill = new(10, 12, 20, 200);
    public static readonly Color PanelEdge = new(255, 255, 255, 30);
    public static readonly Color PanelFillHover = new(22, 26, 40, 225);
    public static readonly Color Shadow = new(0, 0, 0, 90);
    public static readonly Color TrackDark = new(10, 12, 20, 140);

    // The dim wash behind the paused screens (stats, upgrades, slot picking).
    public static readonly Color ScreenDim = new(15, 15, 25, 215);

    // Edge indicators (plans/OFF_SCREEN_INDICATORS.md): enemies point in threat red, and every
    // pickup indicator carries the exact colour its pickup draws with in the world, so the marker
    // reads as "that blue thing is XP" straight off the colour. The guided round's orange comes
    // from WorldRenderer.AmmoColorOf(Orange) directly.
    public static readonly Color EnemyIndicator = new(255, 60, 60, 255);
    public static readonly Color XpPickup = new(50, 150, 255, 255);
    public static readonly Color HealthOrb = new(150, 255, 190, 255);

    // Turret mounts: an empty one is a greyish-blue circle, a mounted gun a greyish-red triangle
    // pointing where it aims, and its firing arc a green sector around the mount.
    public static readonly Color RowHover = new(255, 255, 255, 18);

    public static readonly Color TurretEmptySlot = new(95, 125, 155, 210);
    public static readonly Color TurretMounted = new(190, 95, 95, 255);
    public static readonly Color TurretFits = new(90, 215, 130, 235);

    public static readonly Color TurretArc = new(70, 190, 110, 40);
    public static readonly Color TurretArcHover = new(70, 190, 110, 95);

    // Green → amber → red health gradient shared by the HUD and target hp bars.
    public static Color HealthColor(float ratio) => ratio < 0.5f
        ? Lerp(HealthLow, HealthMid, ratio * 2f)
        : Lerp(HealthMid, HealthFull, (ratio - 0.5f) * 2f);

    public static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t),
            (byte)(a.A + (b.A - a.A) * t));
    }
}
