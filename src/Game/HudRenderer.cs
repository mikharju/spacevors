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

    // Proportional hp bar: a fixed-length track, a colored fill gliding to the current ratio
    // (eased on loss), green→amber→red, and the exact numbers beside it. One constant-cost bar
    // for any max hp — the old pip-per-hp loop drew 40,000 pips in the missile test worlds and
    // put 180ms into the hud.health render pass.
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

        float displayRatio = Math.Clamp(_displayHp / max, 0f, 1f);
        Color healthColor = Palette.HealthColor(Math.Clamp((float)playerHealth.Current / max, 0f, 1f));

        Ui.DrawRoundedRect(paddingX, paddingY, barWidth, barHeight, 3, Palette.TrackDark);
        int fillWidth = (int)(barWidth * displayRatio);
        if (fillWidth > 0)
            Ui.DrawRoundedRect(paddingX, paddingY, fillWidth, barHeight, 3, healthColor);

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

    // The threat readout along the bottom edge: the same row the game logs once a second (ThreatMeter),
    // drawn only when diagnostics mode is on.
    public static void DrawThreatReadout(string line, int windowWidth, int windowHeight)
    {
        const int Font = 14;
        int paddingX = (int)(windowWidth * 0.015f);
        int paddingY = (int)(windowHeight * 0.02f);
        Raylib.DrawText(line, paddingX, windowHeight - paddingY - Font, Font, Palette.TextMuted);
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
