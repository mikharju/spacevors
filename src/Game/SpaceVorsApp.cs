using Raylib_cs;
using Spacevors.Domain.Components;

namespace Spacevors.Game;

public static class SpaceVorsApp
{
    public const int MaxFps = 120;
    const int DefaultWindowWidth = 1920;
    const int DefaultWindowHeight = 1024;

    public static void Main()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(DefaultWindowWidth, DefaultWindowHeight, "SpaceVors");
        Raylib.SetTargetFPS(MaxFps);

        ImageLoader.LoadAssets();
        Lighting.Init();

        var shipSelect = new ShipSelectScreen();
        GameSession? session = null;

        while (!Raylib.WindowShouldClose())
        {
            HandleGlobalKeys();

            if (session is not { } active)
            {
                var chosen = shipSelect.Update(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
                shipSelect.Draw(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());

                if (chosen is { } selected)
                    session = new GameSession(selected);
                continue;
            }

            // Restart from game over: back to ship select.
            if (active.Update())
                session = null;
        }

        Lighting.Shutdown();
        ImageLoader.UnloadAssets();
        Raylib.CloseWindow();
    }

    private static void HandleGlobalKeys()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.F11))
            Raylib.ToggleFullscreen();

        if (Raylib.IsKeyPressed(KeyboardKey.F12))
            Raylib.TakeScreenshot("screenshot.png");
    }
}
