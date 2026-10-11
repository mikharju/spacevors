using Raylib_cs;

namespace Spacevors.Game;

// Small shared immediate-mode UI primitives (plans/UI_POLISH.md): rounded rects,
// translucent panels with soft shadows, easing and fade helpers. No textures —
// four circles round the corners.
public static class Ui
{
    const int ShadowOffset = 2;

    public static void DrawRoundedRect(int x, int y, int width, int height, int radius, Color color)
    {
        radius = Math.Min(radius, Math.Min(width, height) / 2);
        Raylib.DrawRectangle(x + radius, y, width - 2 * radius, height, color);
        Raylib.DrawRectangle(x, y + radius, width, height - 2 * radius, color);
        Raylib.DrawCircle(x + radius, y + radius, radius, color);
        Raylib.DrawCircle(x + width - radius, y + radius, radius, color);
        Raylib.DrawCircle(x + radius, y + height - radius, radius, color);
        Raylib.DrawCircle(x + width - radius, y + height - radius, radius, color);
    }

    // Panel: soft shadow, translucent fill, optional 1px rounded border (outer rounded
    // rect in the border color, fill inset by 1px).
    public static void DrawPanel(int x, int y, int width, int height, int radius, Color fill, Color? border = null, bool shadow = true)
    {
        if (shadow)
            DrawRoundedRect(x + ShadowOffset, y + ShadowOffset, width, height, radius, Palette.Shadow);

        if (border == null)
        {
            DrawRoundedRect(x, y, width, height, radius, fill);
            return;
        }

        DrawRoundedRect(x, y, width, height, radius, border.Value);
        DrawRoundedRect(x + 1, y + 1, width - 2, height - 2, Math.Max(0, radius - 1), fill);
    }

    public static Color WithAlpha(Color color, float alpha)
        => new(color.R, color.G, color.B, (byte)(color.A * Math.Clamp(alpha, 0f, 1f)));

    // Frame-rate independent smoothing toward a target value.
    public static float EaseToward(float current, float target, float perSecond, float frameTime)
        => current + (target - current) * Math.Clamp(perSecond * frameTime, 0f, 1f);

    // 0..1 fade-in progress since startedAt (a Raylib.GetTime() timestamp).
    public static float FadeAlpha(float startedAt, float durationSeconds)
        => Math.Clamp(((float)Raylib.GetTime() - startedAt) / durationSeconds, 0f, 1f);
}
