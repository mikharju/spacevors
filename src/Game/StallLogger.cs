using System.Globalization;
using System.Diagnostics;

namespace Spacevors.Game;

// One `[STALL]` row per frame a player would call a freeze: an absolute threshold, unlike the spike log's
// "unusual for this session" rule, and independent of `SPACEVORS_DIAGNOSTIC` so a report from an ordinary play
// session arrives with its own evidence. Before this, a freeze on the level-up or mount screen left nothing
// behind at all — the spike log was fed only by the running path, and it needs diagnostics mode.
//
// `SPACEVORS_STALL_MS=<n>` moves the threshold; `0` turns the rows off.
static class StallLogger
{
    const string ThresholdEnvVar = "SPACEVORS_STALL_MS";

    // A frame a player feels: measured hitches in this game sit in the tens of milliseconds, so anything past
    // a quarter of a second is the class of stall worth a row of its own — and rare enough to stay readable.
    const double DefaultThresholdMs = 250.0;

    static readonly double _thresholdMs = ReadThresholdMs();

    // Collections *since the previous stall row*: two rows in a row with gc2=1 each is a garbage-collection
    // pause, gc2=0 every time says it is not.
    static int _lastGc0, _lastGc1, _lastGc2;
    static long _lastAllocBytes;
    static int _frames, _stalls;

    // Opened by a played run only (`SpaceVorsApp.RunNormalPlay`): a measured scenario reports every frame through
    // `[PERF]` rows and its own budget gate, and the ceiling probe does not want rows in between its searches.
    static bool _watching;

    public static void Open()
    {
        if (_thresholdMs <= 0) return;
        _watching = true;
        MarkGcCounters();
        Console.WriteLine($"[STALL] watching: frames over {_thresholdMs:F0}ms (SPACEVORS_STALL_MS=0 to silence)");
    }

    public static void Close()
    {
        if (!_watching) return;
        _watching = false;
        Console.WriteLine($"[STALL-END] frames={_frames} stalls={_stalls} thresholdMs={_thresholdMs:F0}");
    }

    // dt/sim/render in ms; `columns` are the caller's world counters and screen name, appended as they are. The
    // worst-pass columns are `RenderTimings`' answer to "which pass ate the frame", which is why the paused
    // renders open a pass chain too.
    public static void RecordIfStalled(double dtMs, double simMs, double renderMs, string columns)
    {
        if (!_watching) return;

        _frames++;
        if (dtMs < _thresholdMs) return;

        _stalls++;
        Console.WriteLine(
            $"[STALL] dt={dtMs:F1} sim={simMs:F1} render={renderMs:F1} other={dtMs - simMs - renderMs:F1} " +
            $"{columns} worst={RenderTimings.WorstPass} subworst={RenderTimings.WorstSubPass} {GcColumns()}");
        MarkGcCounters();
    }

    // A machine-written knob, so parsed invariantly — the same rule the scenario and ceiling seams use. An
    // unparsable value keeps the default rather than silencing the rows, because silence is the failure mode
    // this logger exists to avoid.
    static double ReadThresholdMs() =>
        Environment.GetEnvironmentVariable(ThresholdEnvVar) is not { } raw
            ? DefaultThresholdMs
            : double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double ms)
                ? Math.Max(0, ms)
                : DefaultThresholdMs;

    static string GcColumns()
    {
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        long allocated = GC.GetTotalAllocatedBytes(precise: false);

        return $"gc0={gen0 - _lastGc0} gc1={gen1 - _lastGc1} gc2={gen2 - _lastGc2} allocKb={(allocated - _lastAllocBytes) / 1024}";
    }

    static void MarkGcCounters()
    {
        _lastGc0 = GC.CollectionCount(0);
        _lastGc1 = GC.CollectionCount(1);
        _lastGc2 = GC.CollectionCount(2);
        _lastAllocBytes = GC.GetTotalAllocatedBytes(precise: false);
    }
}
