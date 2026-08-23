using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;

namespace Spacevors.Game;

public static class Renderer
{
    public static void Render(
        EntityManager em,
        float camX, float camY,
        int windowWidth, int windowHeight,
        bool gameOver,
        List<(Vector2 Position, float Size, Color Color, float Parallax)> stars,
        List<(Vector2 Position, float Width, float Height, Color Color)> clutter,
        Entity playerEntity,
        ShipType shipType,
        bool diagnostics)
    {
        Raylib.BeginDrawing();
        DrawScene(em, camX, camY, windowWidth, windowHeight, stars, clutter, playerEntity, shipType, diagnostics);

        if (gameOver)
        {
            HudRenderer.DrawGameOverText(windowWidth, windowHeight);
        }

        Raylib.EndDrawing();
    }

    public static void RenderPaused(
        EntityManager em,
        float camX, float camY,
        int windowWidth, int windowHeight,
        List<(Vector2 Position, float Size, Color Color, float Parallax)> stars,
        List<(Vector2 Position, float Width, float Height, Color Color)> clutter,
        Entity playerEntity,
        ShipType shipType,
        bool diagnostics,
        PendingUpgradeOptions? upgradeOptions,
        int playerLevel,
        int hoveredIndex)
    {
        Raylib.BeginDrawing();
        DrawScene(em, camX, camY, windowWidth, windowHeight, stars, clutter, playerEntity, shipType, diagnostics);
        StatsScreenRenderer.Draw(em, playerEntity, shipType.Name, playerLevel, windowWidth, windowHeight, upgradeOptions, hoveredIndex);
        Raylib.EndDrawing();
    }

    private static void DrawScene(
        EntityManager em,
        float camX, float camY,
        int windowWidth, int windowHeight,
        List<(Vector2 Position, float Size, Color Color, float Parallax)> stars,
        List<(Vector2 Position, float Width, float Height, Color Color)> clutter,
        Entity playerEntity,
        ShipType shipType,
        bool diagnostics)
    {
        Raylib.ClearBackground(new Color(15, 15, 25, 255));

        Lighting.BeginFrame(camX, camY, windowWidth, windowHeight);
        LightGatherer.Collect(em, camX, camY, windowWidth, windowHeight);

        BackgroundRenderer.Draw(stars, clutter, camX, camY, windowWidth, windowHeight);
        WorldRenderer.Draw(em, playerEntity, shipType, diagnostics, camX, camY, windowWidth, windowHeight);
        HudRenderer.DrawHealthBar(em, playerEntity, windowWidth, windowHeight);
    }

}
