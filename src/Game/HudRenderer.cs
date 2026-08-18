using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;

namespace Spacevors.Game;

public static class HudRenderer
{
    public static void DrawHealthBar(EntityManager em, Entity playerEntity, int windowWidth, int windowHeight)
    {
        var playerHealth = em.GetComponent<Health>(playerEntity);
        var playerStats = em.GetComponent<Player>(playerEntity);
        int barWidth = (int)(windowWidth * 0.12f);
        int barHeight = 14;
        int paddingX = (int)(windowWidth * 0.015f);
        int paddingY = (int)(windowHeight * 0.02f);
        float healthPercent = Math.Clamp((float)playerHealth.Current / playerStats.MaxHealth, 0f, 1f);

        Raylib.DrawRectangle(paddingX, paddingY, barWidth, barHeight, new Color(50, 50, 50, 255));
        int filledWidth = (int)(barWidth * healthPercent);
        Color healthColor = filledWidth > barWidth / 3 ? new Color(80, 255, 80, 255) : new Color(255, 60, 60, 255);
        Raylib.DrawRectangle(paddingX, paddingY, filledWidth, barHeight, healthColor);
        Raylib.DrawRectangleLines(paddingX, paddingY, barWidth, barHeight, new Color(180, 180, 180, 255));

        string text = $"{playerHealth.Current}/{playerStats.MaxHealth}";
        int textWidth = Raylib.MeasureText(text, 14);
        Raylib.DrawText(text, paddingX + (barWidth - textWidth) / 2, paddingY + (barHeight - 14) / 2, 14, new Color(255, 255, 255, 255));
    }

    public static void DrawGameOverText(int windowWidth, int windowHeight)
    {
        Raylib.DrawText("GAME OVER", windowWidth / 2 - 80, windowHeight / 2 - 20, 40, new Color(255, 255, 255, 255));
    }
}
