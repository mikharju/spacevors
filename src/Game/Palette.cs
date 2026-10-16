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

    // Turret mounts: an empty one is a greyish-blue circle, a mounted gun a greyish-red triangle
    // pointing where it aims, and its firing arc a green sector around the mount.
    public static readonly Color RowHover = new(255, 255, 255, 18);

    public static readonly Color TurretEmptySlot = new(95, 125, 155, 210);
    public static readonly Color TurretMounted = new(190, 95, 95, 255);
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
