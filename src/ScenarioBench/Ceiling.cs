using System.Diagnostics;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Scenarios;
using Spacevors.Domain.Stats;

// Sim-only ceiling probe (plans/CEILING_TEST.md): the largest world whose median tick still fits the
// frame budget. Nothing here is gated - no baseline file is written, and the exit status is 0 unless the
// probe itself failed - because the point is a number to compare by eye over weeks, not a tripwire.
//
// The search and the shared defaults live in Domain/CeilingProbe.cs so the window host finds the same
// ceiling; what is here is the sim half of a measurement: build a world, tick it, take the median.
public static class Ceiling
{
    public const string CommandName = "ceiling";

    static readonly ShipType Ship = ShipType.Shadow;
    const float FixedDeltaTime = 1f / 120f; // must match the game, as in Program

    public static int Run(string[] args)
    {
        Options options = Parse(args);
        var clock = Stopwatch.StartNew();
        _probes = 0;

        Console.WriteLine($"[CEIL-BEGIN] host=sim mix={CeilingMix.Version} budgetMs={options.BudgetMs:F2} " +
                          $"playableBudgetMs={options.BudgetMs * CeilingProbe.PlayableBudgetFraction:F2} maxCount={options.MaxCount}");

        // One throwaway world first: the earliest ticks of a fresh process pay JIT warm-up, which on
        // the host that produced the recorded numbers inflated the same 2000-entity tick by a third.
        Measure(options.BudgetMs, options.StartCount);

        CeilingBracket full = Search(options, options.BudgetMs);
        // The second bracket re-probes instead of interpolating between the first bracket's points:
        // interpolating would pretend to know the cost curve better than the probe does.
        CeilingBracket playable = Search(options, options.BudgetMs * CeilingProbe.PlayableBudgetFraction);

        Console.WriteLine($"[CEIL-END] host=sim mix={CeilingMix.Version} seconds={clock.Elapsed.TotalSeconds:F1} probes={_probes}");
        Console.WriteLine(CeilingProbe.Headline("sim", CeilingMix.Version, "frame", options.BudgetMs, full));
        Console.WriteLine(CeilingProbe.Headline("sim", CeilingMix.Version, "playable",
            options.BudgetMs * CeilingProbe.PlayableBudgetFraction, playable));
        return 0;
    }

    static CeilingBracket Search(Options options, double budgetMs) => CeilingProbe.Search(
        count => Measure(budgetMs, count), budgetMs, options.StartCount, options.MaxCount, CeilingProbe.Resolution,
        sample => { _probes++; Console.WriteLine(CeilingProbe.Row(sample)); });

    static CeilingSample Measure(double budgetMs, int count)
    {
        // A fresh EntityManager per probe: dropping the old world is the same teardown the bench does
        // between processes, so nothing carries across probes.
        var (em, playerEntity, _) = WorldFactory.Create(Ship, WorldView.DefaultViewportSize, EntityManager.DefaultRngSeed, testAsteroid: true);
        ScenarioWorld.SeedMixedWorld(em, playerEntity, count);

        var view = new WorldView(em);
        var runner = new SimulationRunner();
        var commands = new CommandBuffer();

        int gen2Before = GC.CollectionCount(2);
        var samples = new double[CeilingProbe.MeasuredFrames];
        long liveEntities = 0;

        int warmup = CeilingProbe.WarmupFrames, measured = CeilingProbe.MeasuredFrames;
        for (int tick = 1; tick <= warmup + measured; tick++)
        {
            var sw = Stopwatch.StartNew();
            runner.RunTick(view, em, commands, FixedDeltaTime);
            sw.Stop();
            em.AddElapsedTime(FixedDeltaTime);

            if (tick <= warmup) continue;
            samples[tick - warmup - 1] = sw.Elapsed.TotalMilliseconds;
            liveEntities += Count<Position>(em);
        }

        double median = CeilingProbe.Median(samples);

        return new CeilingSample(count, Math.Round(median, 3), Math.Round(samples.Average(), 3), Math.Round(samples[^1], 3),
            (int)Math.Round(liveEntities / (double)measured), GC.CollectionCount(2) - gen2Before, median <= budgetMs);
    }

    static int _probes;

    static int Count<T>(EntityManager em) where T : notnull =>
        em.TryGetStorage(out ComponentStorage<T> storage) ? storage.Count : 0;

    readonly record struct Options(double BudgetMs, int StartCount, int MaxCount);

    // --budget 8.33 / --start 2000 / --max 131072
    static Options Parse(string[] args)
    {
        double budget = CeilingProbe.BudgetMs;
        int start = CeilingProbe.StartCount, max = CeilingProbe.MaxCount;

        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--budget": budget = double.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--start": start = int.Parse(args[i + 1]); break;
                case "--max": max = int.Parse(args[i + 1]); break;
                default: Console.Error.WriteLine($"unknown ceiling option '{args[i]}'"); break;
            }
        }

        return new Options(budget, start, max);
    }
}
