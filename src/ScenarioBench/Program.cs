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

        Console.WriteLine($"[PERF-BEGIN] scenario={spec.Name} ticks={ticks} bullets={spec.BulletCount} budgetFrameMs={spec.FrameMsBudget:F2}");

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

    static void PrintRow(int tick, double simMs, EntityManager em)
    {
        Console.WriteLine(
            $"[PERF] frame={tick} dt={simMs:F2} sim={simMs:F2} apply=0.00 render=0.00 other=0.00 " +
            $"ents={Count<Position>(em)} ammo={Count<Ammo>(em)} sparks={Count<DamageSpark>(em)} " +
            $"explosions={Count<Explosion>(em)} beams={Count<LaserBeam>(em)} mines={Count<EnemyMine>(em)} " +
            $"ships={Count<EnemyShip>(em)} asteroids={Count<Asteroid>(em)} pickups={Count<XpPickup>(em)}");
    }

    static int Count<T>(EntityManager em) where T : notnull =>
        em.TryGetStorage(out ComponentStorage<T> storage) ? storage.Count : 0;
}
