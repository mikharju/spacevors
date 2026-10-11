using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Game;

public static class HudRenderer
{
    // Eased display hp: lost segments fade out instead of snapping.
    private static float _displayHp = -1f;
    private static float _gameOverStartedAt = -1f;

    // Segmented hp bar: one rounded pip per hp point (hp is a small integer), eased on
    // loss, green→amber→red by ratio, muted numeric readout beside it.
    public static void DrawHealthBar(EntityManager em, Entity playerEntity, int windowWidth, int windowHeight)
    {
        var playerHealth = em.GetComponent<Health>(playerEntity);
        int barWidth = (int)(windowWidth * 0.12f);
        int barHeight = 14;
        int paddingX = (int)(windowWidth * 0.015f);
        int paddingY = (int)(windowHeight * 0.02f);
        int max = Math.Max(1, playerHealth.Max);

        if (_displayHp < 0f || _displayHp > max) _displayHp = playerHealth.Current;
        _displayHp = Ui.EaseToward(_displayHp, playerHealth.Current, 8f, (float)Raylib.GetFrameTime());

        Ui.DrawRoundedRect(paddingX, paddingY, barWidth, barHeight, 3, Palette.TrackDark);

        const float SegmentGap = 2f;
        float segmentWidth = (barWidth - SegmentGap * (max - 1)) / max;
        Color healthColor = Palette.HealthColor(Math.Clamp((float)playerHealth.Current / max, 0f, 1f));

        for (int i = 0; i < max; i++)
        {
            float alpha = Math.Clamp(_displayHp - i, 0f, 1f); // lost segments fade out
            if (alpha <= 0.01f) continue;

            int x = paddingX + (int)(i * (segmentWidth + SegmentGap));
            Ui.DrawRoundedRect(x, paddingY, (int)MathF.Ceiling(segmentWidth), barHeight, 3,
                Ui.WithAlpha(healthColor, alpha));
        }

        string text = $"{playerHealth.Current}/{playerHealth.Max}";
        Raylib.DrawText(text, paddingX + barWidth + 10, paddingY, 14, Palette.TextMuted);
    }

    // Thin top-edge xp bar: full-width track, accent fill for progress toward the next
    // level, muted level readout at the right end.
    public static void DrawXpBar(EntityManager em, Entity playerEntity, int windowWidth, int windowHeight)
    {
        var player = em.GetComponent<Player>(playerEntity);
        float progress = Math.Clamp((float)player.Xp / UpgradeStats.XpForLevel(player.Level), 0f, 1f);

        const int BarHeight = 3;
        Raylib.DrawRectangle(0, 0, windowWidth, BarHeight, Palette.TrackDark);
        if (progress > 0f)
            Raylib.DrawRectangle(0, 0, (int)(windowWidth * progress), BarHeight, Palette.Accent);

        string level = $"LV {player.Level}";
        const int Font = 12;
        Raylib.DrawText(level, windowWidth - Raylib.MeasureText(level, Font) - 10, 6, Font, Palette.TextMuted);
    }

    // Fades a dark overlay in over the scene, then the title, accent rule and restart hint.
    public static void DrawGameOverText(int windowWidth, int windowHeight)
    {
        if (_gameOverStartedAt < 0f) _gameOverStartedAt = (float)Raylib.GetTime();
        float fade = Ui.FadeAlpha(_gameOverStartedAt, 0.5f);

        Raylib.DrawRectangle(0, 0, windowWidth, windowHeight, new Color((byte)0, (byte)0, (byte)0, (byte)(140 * fade)));

        const string Text = "GAME OVER";
        const int FontSize = 40;
        int textWidth = Raylib.MeasureText(Text, FontSize);
        int x = windowWidth / 2 - textWidth / 2;
        int y = windowHeight / 2 - 40;
        Raylib.DrawText(Text, x, y, FontSize, Ui.WithAlpha(Palette.TextBright, fade));
        Raylib.DrawRectangle(x, y + FontSize + 6, textWidth, 2, Ui.WithAlpha(Palette.Accent, 0.8f * fade));

        const string Hint = "Press R to restart";
        const int HintSize = 18;
        Raylib.DrawText(Hint, windowWidth / 2 - Raylib.MeasureText(Hint, HintSize) / 2, y + FontSize + 24,
            HintSize, Ui.WithAlpha(Palette.TextMuted, fade));
    }

    public static void ResetGameOverFade() => _gameOverStartedAt = -1f;
}
