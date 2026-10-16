using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

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
        bool diagnostics,
        string threatLine)
    {
        Raylib.BeginDrawing();
        RenderTimings.BeginFrame();
        DrawScene(em, camX, camY, windowWidth, windowHeight, stars, clutter, playerEntity, shipType, diagnostics, threatLine);

        if (gameOver)
        {
            HudRenderer.DrawGameOverText(windowWidth, windowHeight);
        }
        else
        {
            HudRenderer.ResetGameOverFade();
        }
        RenderTimings.Pass("hud.gameOverFade");

        Raylib.EndDrawing();
        RenderTimings.Pass("present");
    }

    // The paused stats/upgrade overlay. Prepared once per frame so hover hit-testing and drawing
    // share one layout and scale.
    public readonly record struct StatsOverlay(
        StatsScreenRenderer.Prepared Screen,
        PendingUpgradeOptions? Options,
        int PlayerLevel,
        int HoveredIndex,
        bool CanManageMounts,
        bool HoveredManage);

    public static void RenderPaused(
        EntityManager em,
        float camX, float camY,
        int windowWidth, int windowHeight,
        List<(Vector2 Position, float Size, Color Color, float Parallax)> stars,
        List<(Vector2 Position, float Width, float Height, Color Color)> clutter,
        Entity playerEntity,
        ShipType shipType,
        bool diagnostics,
        StatsOverlay stats)
    {
        Raylib.BeginDrawing();
        DrawScene(em, camX, camY, windowWidth, windowHeight, stars, clutter, playerEntity, shipType, diagnostics);
        StatsScreenRenderer.Draw(stats.Screen, shipType.Name, stats.PlayerLevel, windowWidth, windowHeight, stats.Options, stats.HoveredIndex, stats.CanManageMounts, stats.HoveredManage);
        Raylib.EndDrawing();
    }

    // The hull-slot picking screen: the world behind, the hull drawn large, its mounts and arcs on top.
    public static void RenderMount(
        EntityManager em,
        float camX, float camY,
        int windowWidth, int windowHeight,
        List<(Vector2 Position, float Size, Color Color, float Parallax)> stars,
        List<(Vector2 Position, float Width, float Height, Color Color)> clutter,
        Entity playerEntity,
        bool diagnostics,
        MountMenuRenderer.Prepared mount,
        int hoveredSlot,
        int hoveredCargo)
    {
        Raylib.BeginDrawing();
        DrawScene(em, camX, camY, windowWidth, windowHeight, stars, clutter, playerEntity, mount.Ship, diagnostics);
        MountMenuRenderer.Draw(mount, hoveredSlot, hoveredCargo, windowWidth, windowHeight);
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
        bool diagnostics,
        string threatLine = "")
    {
        Raylib.ClearBackground(new Color(15, 15, 25, 255));
        RenderTimings.Pass("clear");

        Lighting.BeginFrame(camX, camY, windowWidth, windowHeight);
        RenderTimings.Pass("lighting.begin");
        LightGatherer.Collect(em, camX, camY, windowWidth, windowHeight);
        RenderTimings.Pass("lights.collect");

        BackgroundRenderer.Draw(stars, clutter, camX, camY, windowWidth, windowHeight);
        RenderTimings.Pass("background");

        // WorldRenderer reports its own world.* passes on the same stopwatch, so there is no
        // aggregate "world" line: summing those passes gives it.
        WorldRenderer.Draw(em, playerEntity, shipType, diagnostics, camX, camY, windowWidth, windowHeight);

        HudRenderer.DrawHealthBar(em, playerEntity, windowWidth, windowHeight);
        RenderTimings.Pass("hud.health");
        HudRenderer.DrawXpBar(em, playerEntity, windowWidth, windowHeight);
        RenderTimings.Pass("hud.xp");

        // Empty when diagnostics mode is off: the caller owns the gate.
        if (threatLine.Length > 0)
        {
            HudRenderer.DrawThreatReadout(threatLine, windowWidth, windowHeight);
            RenderTimings.Pass("hud.threat");
        }
    }

}
