using System.Diagnostics;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Scenarios;

// Sim-only scenario runner: no window, no renderer, so the numbers are pure ECS cost and are not
// polluted by the GPU or by a software rasteriser. Prints the same [PERF] rows as the game with
// render=0, so .tools/scenario.sh aggregates either host.
//
// Usage: ScenarioBench <scenario> [ticks]
public static class Program
{
    // Must match the game's fixed timestep (SpaceVorsApp.MaxFps); scenarios are defined in ticks.
    const float FixedDeltaTime = 1f / 120f;

    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine($"usage: ScenarioBench <scenario> [ticks]; known: {Scenarios.Names}");
            return 2;
        }

        ScenarioSpec? spec = Scenarios.Find(args[0]);
        if (spec is null)
        {
            Console.Error.WriteLine($"unknown scenario '{args[0]}'; known: {Scenarios.Names}");
            return 2;
        }

        int ticks = args.Length > 1 && int.TryParse(args[1], out int parsed) ? parsed : spec.MeasureTicks;
        return Run(spec, ticks);
    }

    static int Run(ScenarioSpec spec, int ticks)
    {
        // WorldView starts with the pinned viewport and a centred mouse, which is what a scenario wants.
        var (em, playerEntity, _) = WorldFactory.Create(spec.Ship, WorldView.DefaultViewportSize, spec.Seed, testAsteroid: true);
        var view = new WorldView(em);
        ScenarioWorld.Apply(em, playerEntity, spec);

        Console.WriteLine($"[PERF-BEGIN] scenario={spec.Name} ticks={ticks} bullets={spec.BulletCount} budgetFrameMs={spec.FrameMsBudget:F2} expectedOverBudget={spec.ExpectedOverBudget}");

        _lastGc0 = GC.CollectionCount(0);
        _lastGc1 = GC.CollectionCount(1);
        _lastGc2 = GC.CollectionCount(2);
        _lastAllocBytes = GC.GetTotalAllocatedBytes(precise: false);

        var runner = new SimulationRunner();
        var commands = new CommandBuffer();
        var sw = new Stopwatch();

        for (int tick = 1; tick <= ticks; tick++)
        {
            sw.Restart();
            runner.RunTick(view, em, commands, FixedDeltaTime);
            sw.Stop();

            PrintRow(tick, sw.Elapsed.TotalMilliseconds, em);
            em.AddElapsedTime(FixedDeltaTime);
        }

        Console.WriteLine($"[PERF-END] scenario={spec.Name} budgetFrameMs={spec.FrameMsBudget:F2}");
        return 0;
    }

    // Same row format as GameSession.ReportScenarioFrame: keep the two in step. render is always 0
    // here, which is the point - what is left is pure ECS cost.
    static void PrintRow(int tick, double simMs, EntityManager em)
    {
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        long allocated = GC.GetTotalAllocatedBytes(precise: false);

        Console.WriteLine(
            $"[PERF] frame={tick} dt={simMs:F2} sim={simMs:F2} apply=0.00 render=0.00 other=0.00 " +
            $"ents={Count<Position>(em)} ammo={Count<Ammo>(em)} sparks={Count<DamageSpark>(em)} " +
            $"explosions={Count<Explosion>(em)} beams={Count<LaserBeam>(em)} mines={Count<EnemyMine>(em)} " +
            $"ships={Count<EnemyShip>(em)} asteroids={Count<Asteroid>(em)} pickups={Count<XpPickup>(em)} " +
            $"gc0={gen0 - _lastGc0} gc1={gen1 - _lastGc1} gc2={gen2 - _lastGc2} allocKb={(allocated - _lastAllocBytes) / 1024}");

        _lastGc0 = gen0;
        _lastGc1 = gen1;
        _lastGc2 = gen2;
        _lastAllocBytes = allocated;
    }

    static int _lastGc0, _lastGc1, _lastGc2;
    static long _lastAllocBytes;

    static int Count<T>(EntityManager em) where T : notnull =>
        em.TryGetStorage(out ComponentStorage<T> storage) ? storage.Count : 0;
}
