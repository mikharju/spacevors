using System.Diagnostics;
using Spacevors.Domain;
using Spacevors.Domain.Scenarios;
using Xunit;

// Scenario-driven performance test: the same worlds ScenarioBench and .tools/scenario.sh measure,
// driven headless through the real fixed-timestep tick. No renderer, so the numbers are pure ECS cost.
//
// Two assertions per scenario, because they answer differently:
//
//  * Median tick time. Machine-bound, so it gets a ceiling per host that runs this suite (see
//    Budgets). These are characterization ceilings: a generous multiple of the worst median observed
//    on that host. They catch collapses - an accidental O(n^2) sweep, a system that started scanning
//    everything - and they are deliberately not the game's 8.33ms frame budget, which belongs to a
//    machine with a GPU and is gated by .tools/scenario.sh against the real display.
//
//  * Allocated bytes per tick. Nearly host-independent, so one ceiling serves every machine, and it
//    catches the regression class that actually hurts an ECS: allocating inside a system. GC pressure
//    showed up in measured runs as ~1.2ms per collecting frame, so this is the assertion worth
//    trusting on a shared CI runner.
//
// Ceilings may only be tightened. Loosening one needs the host and the measured median in the commit
// message, or the gate quietly becomes the thing it replaced: three phases of "currently too slow".
public class ScenarioPerformanceTest
{
    // Must match the game's fixed timestep (SpaceVorsApp.MaxFps); scenarios are defined in ticks.
    const float FixedDeltaTime = 1f / 120f;

    // Their sum is the specs' MeasureTicks, so the window below is the window the scenario harness
    // measures: the first ticks are dropped as JIT and allocation warm-up.
    const int WarmupTicks = 20;
    const int MeasuredTicks = 100;

    // The CI runner is about 3x slower on single-core work than the desktop that wrote these numbers
    // (ammo-spray: 5.28ms median on CI against 1.73ms locally), and its runner is shared. The CI set
    // is the desktop set times that ratio, rounded; tightening it needs a CI measurement.
    const string CiHostMarker = "SPACEVORS_PERF_HOST";

    // AllocKbPerTick is measured over the measured window with GC.GetTotalAllocatedBytes, and it is
    // the portable number: the test host measured ammo-spray at 289.9KB and impact-storm at 746.2KB
    // per tick, and the real-GPU harness measured 296KB and 753KB per frame for the same scenarios.
    // Budgets are those values with ~1.3x room for measurement noise, and nothing more. The gap
    // between ammo-spray (290KB) and ammo-spray-miss (10KB) says where the allocation lives: impacts
    // spawning effect entities, not bullets existing.
    static readonly Budget[] AllBudgets =
    [
        new("baseline-idle", DesktopMs: 0.5, CiMs: 1.5, AllocKbPerTick: 15.0),
        new("hull-damage", DesktopMs: 1.0, CiMs: 3.0, AllocKbPerTick: 16.0),
        new("ammo-spray", DesktopMs: 5.0, CiMs: 15.0, AllocKbPerTick: 400.0),
        new("ammo-spray-miss", DesktopMs: 2.5, CiMs: 7.5, AllocKbPerTick: 16.0),
        new("impact-storm", DesktopMs: 5.0, CiMs: 15.0, AllocKbPerTick: 1000.0),
        // The two hull worlds (plans/NON_CIRCLE_COLLISION.md): hull-stream is ammo-spray plus one hull
        // standing in the stream, so it takes the same band; hull-crowding has 42 hulls and no bullets,
        // and this host measured 0.29ms median against 0.22KB per tick.
        new("hull-stream", DesktopMs: 5.0, CiMs: 15.0, AllocKbPerTick: 400.0),
        new("hull-crowding", DesktopMs: 1.5, CiMs: 4.5, AllocKbPerTick: 40.0),
        // The two missile worlds (plans/MISSILE_PERF.md): ammo-spray plus guided rounds. This host
        // measured 1.79ms (384) and 3.02ms (1024) sim median at --iters 3, so the desktop band
        // follows ammo-spray's 2.5x headroom over the observed number.
        new("missile-384", DesktopMs: 5.0, CiMs: 15.0, AllocKbPerTick: 450.0),
        new("missile-1024", DesktopMs: 8.0, CiMs: 24.0, AllocKbPerTick: 500.0),
    ];

    public static IEnumerable<object[]> Budgets =>
        AllBudgets.Select(budget => new object[] { budget });

    public readonly record struct Budget(string Scenario, double DesktopMs, double CiMs, double AllocKbPerTick);

    static bool RunsOnCi => Environment.GetEnvironmentVariable(CiHostMarker) == "ci";

    [Theory]
    [MemberData(nameof(Budgets))]
    public void Scenario_MedianTickTime_StaysUnderCeiling(Budget budget)
    {
        (double medianMs, double allocKb) = Measure(Scenarios.Find(budget.Scenario)!);
        double ceilingMs = RunsOnCi ? budget.CiMs : budget.DesktopMs;

        Assert.True(medianMs <= ceilingMs,
            $"{budget.Scenario}: median tick {medianMs:F2}ms exceeds the {ceilingMs:F2}ms ceiling on a " +
            $"host with {Environment.ProcessorCount} logical CPUs ({(RunsOnCi ? "CI" : "desktop")} set)");
    }

    [Theory]
    [MemberData(nameof(Budgets))]
    public void Scenario_AllocationPerTick_StaysUnderBudget(Budget budget)
    {
        (double medianMs, double allocKb) = Measure(Scenarios.Find(budget.Scenario)!);

        Assert.True(allocKb <= budget.AllocKbPerTick,
            $"{budget.Scenario}: {allocKb:F1}KB allocated per tick exceeds the " +
            $"{budget.AllocKbPerTick:F0}KB budget (median tick {medianMs:F2}ms)");
    }

    static (double MedianMs, double AllocKbPerTick) Measure(ScenarioSpec spec)
    {
        var (em, playerEntity, _) = WorldFactory.Create(spec.Ship, WorldView.DefaultViewportSize, spec.Seed, testAsteroid: true);
        ScenarioWorld.Apply(em, playerEntity, spec);

        var view = new WorldView(em);
        var runner = new SimulationRunner();
        var commands = new CommandBuffer();
        var stopwatch = new Stopwatch();
        var measured = new double[MeasuredTicks];
        long allocStart = 0;

        for (int tick = 1; tick <= WarmupTicks + MeasuredTicks; tick++)
        {
            // Count allocations only over the measured window, warm-up excluded with the JIT.
            if (tick == WarmupTicks + 1) allocStart = GC.GetTotalAllocatedBytes(precise: true);

            stopwatch.Restart();
            runner.RunTick(view, em, commands, FixedDeltaTime);
            stopwatch.Stop();

            if (tick > WarmupTicks) measured[tick - WarmupTicks - 1] = stopwatch.Elapsed.TotalMilliseconds;
            em.AddElapsedTime(FixedDeltaTime);
        }

        double allocKbPerTick = (GC.GetTotalAllocatedBytes(precise: true) - allocStart) / 1024.0 / MeasuredTicks;

        Array.Sort(measured);
        return (Median(measured), allocKbPerTick);
    }

    static double Median(double[] sorted)
    {
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2.0 : sorted[middle];
    }
}
