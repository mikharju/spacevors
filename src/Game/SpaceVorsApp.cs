using System.Diagnostics;
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

    // Ceiling probe seam (plans/CEILING_TEST.md): SPACEVORS_CEILING=<budgetMs> searches for the largest
    // world that still fits that budget in this window; _START and _MAX bound the search. The budget is
    // parsed invariantly because it is a machine-written number, not a locale-dependent one.
    const string CeilingEnvVar = "SPACEVORS_CEILING";
    const string CeilingStartEnvVar = "SPACEVORS_CEILING_START";
    const string CeilingMaxEnvVar = "SPACEVORS_CEILING_MAX";

    public static void Main()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(DefaultWindowWidth, DefaultWindowHeight, "SpaceVors");

        ImageLoader.LoadAssets();
        Lighting.Init();
        SpikeLogger.Open();

        if (TryCeilingFromEnvironment(out double ceilingBudgetMs, out int ceilingStart, out int ceilingMax))
            RunCeiling(ceilingBudgetMs, ceilingStart, ceilingMax);
        else if (TryScenarioFromEnvironment(out var scenario))
            RunScenario(scenario!);
        else
            RunNormalPlay();

        Lighting.Shutdown();
        ImageLoader.UnloadAssets();
        SpikeLogger.Close();
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

    // The window half of the ceiling probe (plans/CEILING_TEST.md): the same search the headless bench
    // runs, but every measurement is a real frame with the renderer in it, so the number includes the
    // draw path. The window opens once and each probe swaps in a fresh world - that is what keeps the
    // search in seconds, because paying process startup once per probe the way the scenario harness pays
    // it per iteration would dominate the whole run.
    private static void RunCeiling(double budgetMs, int startCount, int maxCount)
    {
        // Uncapped: a cap sleeps inside EndDraw, which would hide the very cost being searched for.
        Raylib.SetTargetFPS(0);
        var clock = Stopwatch.StartNew();
        _probes = 0;

        // One throwaway world, unreported: the first frames of a fresh process pay shader compilation
        // and the first texture uploads - the black screen before frame 1 - and would otherwise read as
        // the cost of the smallest probe.
        MeasureProbe(budgetMs, startCount);

        Console.WriteLine($"[CEIL-BEGIN] host=window mix={CeilingMix.Version} budgetMs={budgetMs:F2} " +
                          $"playableBudgetMs={budgetMs * CeilingProbe.PlayableBudgetFraction:F2} maxCount={maxCount}");

        CeilingBracket full = Probe(budgetMs, startCount, maxCount);
        CeilingBracket playable = Probe(budgetMs * CeilingProbe.PlayableBudgetFraction, startCount, maxCount);

        Console.WriteLine($"[CEIL-END] host=window mix={CeilingMix.Version} seconds={clock.Elapsed.TotalSeconds:F1} probes={_probes}");
        Console.WriteLine(CeilingProbe.Headline("window", CeilingMix.Version, "frame", budgetMs, full));
        Console.WriteLine(CeilingProbe.Headline("window", CeilingMix.Version, "playable",
            budgetMs * CeilingProbe.PlayableBudgetFraction, playable));
    }

    private static CeilingBracket Probe(double budgetMs, int startCount, int maxCount) => CeilingProbe.Search(
        count => MeasureProbe(budgetMs, count), budgetMs, startCount, maxCount, CeilingProbe.Resolution,
        sample => { _probes++; Console.WriteLine(CeilingProbe.Row(sample)); });

    private static CeilingSample MeasureProbe(double budgetMs, int count)
    {
        ScenarioSpec spec = CeilingSpec(count);
        var session = new GameSession(spec.Ship, spec);

        int gen2Before = GC.CollectionCount(2);
        while (!Raylib.WindowShouldClose() && !session.ScenarioFinished) session.Update();

        double[] frames = DropWarmup(session.ScenarioFrameMs);
        if (frames.Length == 0) return new CeilingSample(count, 0, 0, 0, 0, 0, Pass: false);

        double median = CeilingProbe.Median(frames);
        int live = (int)Math.Round(session.ScenarioLiveEntities.Skip(CeilingProbe.WarmupFrames).DefaultIfEmpty(0).Average());

        return new CeilingSample(count, Math.Round(median, 3), Math.Round(frames.Average(), 3), Math.Round(frames[^1], 3),
            live, GC.CollectionCount(2) - gen2Before, median <= budgetMs);
    }

    // The probe borrows baseline-idle, which seeds no volley of its own, and swaps in the pinned ceiling
    // mix. ExpectedOverBudget because the search deliberately walks past the budget to bracket it.
    private static ScenarioSpec CeilingSpec(int count) =>
        Scenarios.Find("baseline-idle")! with
        {
            Name = $"ceiling-{count}",
            CeilingCount = count,
            MeasureTicks = CeilingProbe.WarmupFrames + CeilingProbe.MeasuredFrames,
            ExpectedOverBudget = true,
        };

    // The first frames of a probe belong to the world build; the sim host drops the same number.
    private static double[] DropWarmup(IReadOnlyList<double> frames) =>
        frames.Count > CeilingProbe.WarmupFrames
            ? frames.Skip(CeilingProbe.WarmupFrames).ToArray()
            : frames.ToArray();

    private static bool TryCeilingFromEnvironment(out double budgetMs, out int startCount, out int maxCount)
    {
        budgetMs = 0; startCount = 0; maxCount = 0;
        string value = Environment.GetEnvironmentVariable(CeilingEnvVar) ?? "";

        if (!double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out budgetMs) || budgetMs <= 0) return false;

        startCount = ReadIntEnv(CeilingStartEnvVar, CeilingProbe.StartCount);
        maxCount = ReadIntEnv(CeilingMaxEnvVar, CeilingProbe.MaxCount);
        return true;
    }

    private static int _probes;

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
