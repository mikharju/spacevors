using System.Diagnostics;
using Spacevors.Domain;

namespace Spacevors.Game;

// The spike log: one line per frame that took far longer than the frames around it, so a long
// session leaves a dozen lines instead of two million stdout rows. Diagnostics mode only: Open()
// runs at startup and Close() prints the tally. Where the file lives — beside the binary by default,
// SPACEVORS_SPIKE_DIR over it, files older than KeepFor deleted before the run starts writing — is
// DiagnosticsFile's rule, shared with every other diagnostics file the game writes.
static class SpikeLogger
{
    // A frame is a spike when it reaches this multiple of the window median **and** the floor:
    // under the floor a frame is merely busy, not something a player feels as a freeze.
    const double SpikeFactor = 4.0;
    const double SpikeFloorMs = 25.0;

    // About 2 s of history at 120 fps. A median rather than a mean, because a freeze would drag
    // a mean toward itself and hide the frames that were actually slow.
    const int WindowFrames = 256;

    // Below this the median is too thin to judge anything against, and the startup hitch on
    // frame 1 would be reported every single run.
    const int WarmupFrames = 64;

    const string DirEnvVar = "SPACEVORS_SPIKE_DIR";
    static readonly TimeSpan KeepFor = TimeSpan.FromDays(1);

    static readonly double[] _window = new double[WindowFrames];
    static readonly double[] _sorted = new double[WindowFrames];
    static int _frames, _spikes;
    static StreamWriter? _file;

    static int _lastGc0, _lastGc1, _lastGc2;
    static long _lastAllocBytes;

    public static bool IsActive => _file != null;

    public static void Open()
    {
        if (!DiagnosticLogger.IsEnabled) return;

        _file = DiagnosticsFile.Open(DirEnvVar, "spikes", KeepFor, out string? path);
        if (_file == null) return;

        Console.WriteLine(
            $"[SPIKE] log: {path} factor={SpikeFactor:F0} floorMs={SpikeFloorMs:F0} " +
            $"windowFrames={WindowFrames} keepDays={KeepFor.TotalDays:F0}");
    }

    public static void Close()
    {
        if (_file == null) return;

        Console.WriteLine($"[SPIKE-END] frames={_frames} spikes={_spikes}");
        _file.Dispose();
        _file = null;
    }

    // dt/sim/render in ms; `columns` is the caller's world counters, appended as they are.
    public static void RecordFrame(double dtMs, double simMs, double renderMs, string columns)
    {
        if (_file == null) return;

        _window[_frames % WindowFrames] = dtMs;
        _frames++;

        double median = _frames >= WarmupFrames ? MedianOfWindow() : 0.0;
        if (_frames >= WarmupFrames && dtMs >= SpikeFloorMs && dtMs >= median * SpikeFactor)
        {
            _spikes++;
            _file.WriteLine(
                $"[SPIKE] frame={_frames} dt={dtMs:F1} sim={simMs:F1} render={renderMs:F1} " +
                $"other={dtMs - simMs - renderMs:F1} median={median:F1} {columns} " +
                $"worst={RenderTimings.WorstPass} subworst={RenderTimings.WorstSubPass} " +
                $"litBlock={LitGroupRenderer.WorstLitBlock} newLit={LitGroupRenderer.NewTextures} {GcColumns()}");
        }

        MarkGcCounters();
    }

    static double MedianOfWindow()
    {
        int n = Math.Min(_frames, WindowFrames);
        Array.Copy(_window, _sorted, n);
        Array.Sort(_sorted, 0, n);
        return n % 2 == 1 ? _sorted[n / 2] : 0.5 * (_sorted[n / 2 - 1] + _sorted[n / 2]);
    }

    // Collections *in this frame*: a spike whose gc2 is 0 is not a garbage-collection pause.
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
