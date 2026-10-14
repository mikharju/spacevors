using System.Diagnostics;
using Spacevors.Domain;
using Spacevors.Domain.Scenarios;
using Xunit;

// Scenario-driven performance test: the same worlds ScenarioBench and .tools/scenario.sh measure,
// driven headless through the real fixed-timestep tick. No renderer, so the number is pure ECS cost.
//
// The spec's FrameMsBudget (1000/120 = 8.33ms) is deliberately not asserted here: a dev desktop, CI
// and a software-rendered sandbox differ in speed by more than that budget, and a perf test that
// fails because of the machine rather than the code is worse than no perf test. The budgets stay in
// the specs, where the .tools harness reports against them.
//
// The ceilings below are characterization ceilings: a generous multiple (>= 2.5x, rounded up) of the
// median each scenario measured on this machine, so they trip only on a large sim regression inside
// `dotnet test`. They may only ever be tightened, never loosened, without a stated reason.
public class ScenarioPerformanceTest
{
    // Must match the game's fixed timestep (SpaceVorsApp.MaxFps); scenarios are defined in ticks.
    const float FixedDeltaTime = 1f / 120f;

    // Their sum is the specs' MeasureTicks, so the window below is the window the scenario harness
    // measures: the first ticks are dropped as JIT and allocation warm-up.
    const int WarmupTicks = 20;
    const int MeasuredTicks = 100;

    // Worst median observed per scenario across the hosts that run this suite, times at least 2.5,
    // rounded up. The hosts matter: the CI runner measured ammo-spray at 5.28ms median where a dev
    // desktop sees 1.73ms, so a ceiling derived from one machine fails on hardware instead of code.
    // Known medians: baseline-idle 0.10, hull-damage 0.28, ammo-spray 5.28 (CI) / 1.73 (desktop),
    // ammo-spray-miss 0.86, impact-storm 1.94. Tightening one needs a rerun on every host in the list.
    const double BaselineIdleCeilingMs = 0.5;
    const double HullDamageCeilingMs = 1.0;
    const double AmmoSprayCeilingMs = 15.0;
    const double AmmoSprayMissCeilingMs = 2.5;
    const double ImpactStormCeilingMs = 5.0;

    [Theory]
    [InlineData("baseline-idle", BaselineIdleCeilingMs)]
    [InlineData("hull-damage", HullDamageCeilingMs)]
    [InlineData("ammo-spray", AmmoSprayCeilingMs)]
    [InlineData("ammo-spray-miss", AmmoSprayMissCeilingMs)]
    [InlineData("impact-storm", ImpactStormCeilingMs)]
    public void Scenario_MedianTickTime_StaysUnderCeiling(string scenario, double ceilingMs)
    {
        ScenarioSpec spec = Scenarios.Find(scenario)!;

        double medianMs = MedianTickMs(spec);

        // Printed so a run shows drift long before a ceiling is reached.
        Console.WriteLine($"[PERF] scenario={spec.Name} medianTickMs={medianMs:F3} ceilingMs={ceilingMs:F2}");

        Assert.True(medianMs <= ceilingMs,
            $"{spec.Name}: median tick {medianMs:F2}ms exceeds the {ceilingMs:F2}ms characterization " +
            $"ceiling on a host with {Environment.ProcessorCount} logical CPUs - check the medians " +
            "listed with the ceilings before assuming the code got slower");
    }

    private static double MedianTickMs(ScenarioSpec spec)
    {
        var (em, playerEntity, _) = WorldFactory.Create(spec.Ship, WorldView.DefaultViewportSize, spec.Seed, testAsteroid: true);
        ScenarioWorld.Apply(em, playerEntity, spec);

        var view = new WorldView(em);
        var runner = new SimulationRunner();
        var commands = new CommandBuffer();
        var stopwatch = new Stopwatch();
        var measured = new double[MeasuredTicks];

        for (int tick = 1; tick <= WarmupTicks + MeasuredTicks; tick++)
        {
            stopwatch.Restart();
            runner.RunTick(view, em, commands, FixedDeltaTime);
            stopwatch.Stop();

            if (tick > WarmupTicks) measured[tick - WarmupTicks - 1] = stopwatch.Elapsed.TotalMilliseconds;
            em.AddElapsedTime(FixedDeltaTime);
        }

        Array.Sort(measured);
        return Median(measured);
    }

    private static double Median(double[] sorted)
    {
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2.0 : sorted[middle];
    }
}
