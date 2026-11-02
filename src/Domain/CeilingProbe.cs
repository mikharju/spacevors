namespace Spacevors.Domain;

// The search shared by both ceiling hosts (plans/CEILING_TEST.md): the headless bench measures a tick,
// the window measures a frame, and both need the same way of finding the largest world that still fits
// a budget. Counts are probed rather than ramped - doubling until the first failure and then bisecting
// costs a few dozen measurements in total, while ramping one entity at a time would pay the sum of
// every count on the way to 100k, which is minutes for one number.
//
// This is measurement plumbing, not game logic, which is why it sits beside DiagnosticLogger and knows
// nothing about what it is measuring: the host supplies the measurement.
public static class CeilingProbe
{
    // Defaults shared by both hosts so the sim and window numbers mean the same thing.
    // Budget: 120 fps, the game's fixed timestep - a frame longer than this drops a frame.
    public const double BudgetMs = 1000d / 120d;

    // A sim-only run that spends the *whole* frame on the sim is not playable, so the sim host also
    // reports the count that leaves half the frame for rendering. Half because the recorded window
    // scenarios put sim at 0.23-0.58 of frame time; the convention is written into every row.
    public const double PlayableBudgetFraction = 0.5;

    public const int StartCount = 2000;
    public const int MaxCount = 131_072;

    // Stop bisecting once the bracket is within 5% of the passing count: the window host is only
    // reliable to about that anyway, and a finer bracket would be measuring noise.
    public const double Resolution = 0.05;

    // The first frames of a probe pay the world build's allocation churn. Twelve measured frames give a
    // median stable to a few percent and cost 0.1 s at budget.
    public const int WarmupFrames = 2;
    public const int MeasuredFrames = 12;

    public static CeilingBracket Search(Func<int, CeilingSample> measure, double budgetMs, int startCount, int maxCount,
                                 double resolution, Action<CeilingSample>? report = null)
    {
        CeilingSample pass = default, fail = default;
        bool anyPass = false, anyFail = false;
        int low = 0, high = 0, count = startCount;

        while (true)
        {
            CeilingSample sample = measure(count);
            report?.Invoke(sample);

            if (sample.Pass)
            {
                pass = sample; anyPass = true; low = count;
                int next = Math.Min(count * 2, maxCount);
                if (next <= count) break; // the whole range fits: nothing to bracket
                count = next;
                continue;
            }

            fail = sample; anyFail = true; high = count;
            break;
        }

        if (!anyPass) return new CeilingBracket(pass, fail, true);  // even the smallest world missed the budget
        if (!anyFail) return new CeilingBracket(pass, pass, false); // the whole range fits the budget

        while (high - low > Math.Max(1, low * resolution))
        {
            int middle = low + (high - low) / 2;
            CeilingSample sample = measure(middle);
            report?.Invoke(sample);

            if (sample.Pass) { pass = sample; low = middle; } else { fail = sample; high = middle; }
        }

        return new CeilingBracket(pass, fail, true);
    }

    // One line per probe, and one per final bracket, in the format .tools/ceiling.sh parses. Both hosts
    // print the same shape so a sim row and a window row can sit in the same table.
    public static string Row(CeilingSample sample) =>
        $"[CEIL] count={sample.Count} medianMs={sample.MedianMs:F3} meanMs={sample.MeanMs:F3} maxMs={sample.MaxMs:F3} " +
        $"ents={sample.LiveEntities} gc2={sample.Gen2} verdict={(sample.Pass ? "pass" : "fail")}";

    public static string Headline(string host, string mix, string label, double budgetMs, CeilingBracket bracket)
    {
        CeilingSample pass = bracket.Pass;
        // No pass in range means the search never got past its first count; say so rather than printing
        // count=0, which would read as "the ceiling is zero".
        string ceiling = pass.Count > 0
            ? $"count={pass.Count} medianMs={pass.MedianMs:F3} ents={pass.LiveEntities}"
            : "count=0 medianMs=na ents=na note=no-pass-in-range";

        return $"CEILING host={host} mix={mix} bracket={label} budgetMs={budgetMs:F2} {ceiling} " +
               $"failCount={(bracket.FoundFailure ? bracket.Fail.Count : pass.Count)} " +
               $"hitMax={(bracket.FoundFailure ? "no" : "yes")}";
    }

    // Median of a host's per-frame times, sorted in place. Median rather than mean because one
    // collection spike must not decide where the ceiling is.
    public static double Median(Span<double> samples)
    {
        samples.Sort();
        return samples[samples.Length / 2];
    }
}

// One measurement of one world size. LiveEntities is what was actually alive during the measured
// frames, which is not exactly the requested count: expiring explosions give birth to smoke.
public readonly record struct CeilingSample(int Count, double MedianMs, double MeanMs, double MaxMs, int LiveEntities, int Gen2, bool Pass);

// The final bracket of a search: the largest measured pass and the smallest measured failure.
// FoundFailure is false when the whole search range passed, which is headroom rather than a ceiling.
public readonly record struct CeilingBracket(CeilingSample Pass, CeilingSample Fail, bool FoundFailure);
