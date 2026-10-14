using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Scenarios;

namespace Spacevors.Game;

public static class SpaceVorsApp
{
    public const int MaxFps = 120;
    const int DefaultWindowWidth = 1920;
    const int DefaultWindowHeight = 1024;

    // Perf-scenario seams. SPACEVORS_SCENARIO=<name> runs a seeded scenario instead of ship select;
    // _CAP sets an fps cap (0 = uncapped), _TICKS overrides the measured length, _HOLD keeps the
    // window open afterwards in seconds. See .tools/scenario.sh.
    const string ScenarioEnvVar = "SPACEVORS_SCENARIO";
    const string ScenarioCapEnvVar = "SPACEVORS_SCENARIO_CAP";
    const string ScenarioTicksEnvVar = "SPACEVORS_SCENARIO_TICKS";
    const string ScenarioHoldEnvVar = "SPACEVORS_SCENARIO_HOLD";
    const double DefaultHoldSeconds = 3.0;

    public static void Main()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(DefaultWindowWidth, DefaultWindowHeight, "SpaceVors");

        ImageLoader.LoadAssets();
        Lighting.Init();

        if (TryScenarioFromEnvironment(out var scenario))
            RunScenario(scenario!);
        else
            RunNormalPlay();

        Lighting.Shutdown();
        ImageLoader.UnloadAssets();
        Raylib.CloseWindow();
    }

    private static void RunNormalPlay()
    {
        Raylib.SetTargetFPS(MaxFps);

        var shipSelect = new ShipSelectScreen(Environment.GetEnvironmentVariable(DiagnosticLogger.EnvVar) == "1");
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
    }

    // Measures the scenario's first MeasureTicks frames from its seeded world, then keeps the window
    // open so a one-second run is still watchable.
    private static void RunScenario(ScenarioSpec scenario)
    {
        // Uncapped by default: with an fps cap raylib sleeps inside EndDraw, which would bury the
        // real render cost and hide headroom.
        Raylib.SetTargetFPS(ReadIntEnv(ScenarioCapEnvVar, 0));

        var session = new GameSession(scenario.Ship, scenario);

        while (!Raylib.WindowShouldClose() && !session.ScenarioFinished)
        {
            HandleGlobalKeys();
            session.Update();
        }

        double hold = ReadDoubleEnv(ScenarioHoldEnvVar, DefaultHoldSeconds);
        if (hold <= 0) return;

        double until = Raylib.GetTime() + hold;
        while (!Raylib.WindowShouldClose() && Raylib.GetTime() < until)
        {
            HandleGlobalKeys();
            session.Update();
        }
    }

    private static bool TryScenarioFromEnvironment(out ScenarioSpec? scenario)
    {
        scenario = null;
        string name = Environment.GetEnvironmentVariable(ScenarioEnvVar) ?? "";
        if (string.IsNullOrWhiteSpace(name)) return false;

        ScenarioSpec? found = Scenarios.Find(name);
        if (found is null)
        {
            DiagnosticLogger.LogWarning($"unknown {ScenarioEnvVar} '{name}'; known scenarios: {Scenarios.Names}");
            return false;
        }

        int ticks = ReadIntEnv(ScenarioTicksEnvVar, 0);
        scenario = ticks > 0 ? found with { MeasureTicks = ticks } : found;
        return true;
    }

    private static int ReadIntEnv(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out int value) ? value : fallback;

    private static double ReadDoubleEnv(string name, double fallback) =>
        double.TryParse(Environment.GetEnvironmentVariable(name), out double value) ? value : fallback;

    private static void HandleGlobalKeys()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.F11))
            Raylib.ToggleFullscreen();

        if (Raylib.IsKeyPressed(KeyboardKey.F12))
            Raylib.TakeScreenshot("screenshot.png");
    }
}
