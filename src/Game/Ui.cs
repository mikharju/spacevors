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

    // Word wrap for the instruction blocks: greedy on spaces, one very long word stays on its own
    // line rather than being cut. Shared by the ship cards and the upgrade screen.
    public static string[] WrapText(string text, int maxWidth, int fontSize)
    {
        var lines = new List<string>();
        string current = "";

        foreach (var word in text.Split(' '))
        {
            string candidate = current.Length == 0 ? word : $"{current} {word}";
            if (current.Length == 0 || Raylib.MeasureText(candidate, fontSize) <= maxWidth)
                current = candidate;
            else
            {
                lines.Add(current);
                current = word;
            }
        }

        if (current.Length > 0) lines.Add(current);
        return lines.ToArray();
    }

    // A key cap: the key's label in a small bordered box. The shape is what makes a control list
    // skimmable in a second — the eye finds the boxes and reads only the words beside them.
    // Returns the x just past the cap so a row can chain several caps without tracking widths.
    public static int DrawKeyCap(int x, int y, string label, int font, Color text, Color fill)
    {
        int padX = Math.Max(4, font / 3);
        int width = Raylib.MeasureText(label, font) + 2 * padX;
        int height = font + font / 4;

        DrawPanel(x, y, width, height, 4, fill, Palette.PanelEdge, shadow: false);
        Raylib.DrawText(label, x + padX, y + (height - font) / 2, font, text);
        return x + width;
    }

    // Frame-rate independent smoothing toward a target value.
    public static float EaseToward(float current, float target, float perSecond, float frameTime)
        => current + (target - current) * Math.Clamp(perSecond * frameTime, 0f, 1f);

    // 0..1 fade-in progress since startedAt (a Raylib.GetTime() timestamp).
    public static float FadeAlpha(float startedAt, float durationSeconds)
        => Math.Clamp(((float)Raylib.GetTime() - startedAt) / durationSeconds, 0f, 1f);
}
