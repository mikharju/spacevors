using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;

namespace Spacevors.Game;

public static class HudRenderer
{
    public static void DrawHealthBar(EntityManager em, Entity playerEntity, int windowWidth, int windowHeight)
    {
        var playerHealth = em.GetComponent<Health>(playerEntity);
        int barWidth = (int)(windowWidth * 0.12f);
        int barHeight = 14;
        int paddingX = (int)(windowWidth * 0.015f);
        int paddingY = (int)(windowHeight * 0.02f);
        float healthPercent = Math.Clamp((float)playerHealth.Current / playerHealth.Max, 0f, 1f);

        Raylib.DrawRectangle(paddingX, paddingY, barWidth, barHeight, new Color(50, 50, 50, 255));
        int filledWidth = (int)(barWidth * healthPercent);
        Color healthColor = filledWidth > barWidth / 3 ? Palette.HealthFull : Palette.HealthLow;
        Raylib.DrawRectangle(paddingX, paddingY, filledWidth, barHeight, healthColor);
        Raylib.DrawRectangleLines(paddingX, paddingY, barWidth, barHeight, new Color(180, 180, 180, 255));

        string text = $"{playerHealth.Current}/{playerHealth.Max}";
        int textWidth = Raylib.MeasureText(text, 14);
        Raylib.DrawText(text, paddingX + (barWidth - textWidth) / 2, paddingY + (barHeight - 14) / 2, 14, new Color(255, 255, 255, 255));
    }

    public static void DrawGameOverText(int windowWidth, int windowHeight)
    {
        const string Text = "GAME OVER";
        const int FontSize = 40;
        Raylib.DrawText(Text, windowWidth / 2 - Raylib.MeasureText(Text, FontSize) / 2, windowHeight / 2 - 20, FontSize, new Color(255, 255, 255, 255));
    }
}
