using Raylib_cs;

namespace Spacevors.Game;

// Colours used by more than one renderer. Everything else stays with the renderer that owns it.
internal static class Palette
{
    // Health bar endpoints: green at full hp, red when nearly dead.
    // HudRenderer switches between them, TargetingRenderer lerps across them.
    public static readonly Color HealthFull = new(80, 255, 80, 255);
    public static readonly Color HealthLow = new(255, 60, 60, 255);
}
