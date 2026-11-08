using System.Diagnostics;
using Spacevors.Domain;

namespace Spacevors.Game;

// Per-pass render timing for the perf scenarios. One reused Stopwatch, pass names are literals, and
// every call is a single branch when diagnostics are off - measuring must not change what is measured.
// The pass named "present" covers EndDrawing, which is also where raylib's SetTargetFPS wait happens.
static class RenderTimings
{
    static readonly Stopwatch _sw = new();
    static readonly Stopwatch _subSw = new();
    static bool _active;
    static long _worstTicks, _worstSubTicks;
    static string _worstName = string.Empty, _worstSubName = string.Empty;

    // The slowest pass of the frame just drawn, e.g. "world.enemyShips:96.7ms" - the spike log's
    // answer to "which pass did the freeze sit in", so the reader does not have to dig through the
    // per-pass rows of that frame. "none" before the first frame is drawn.
    public static string WorstPass => Format(_worstName, _worstTicks);

    // The same for sub-segments (see SubPass): the pass-level number tells you *which* pass, this
    // one tells you which part of it - a lit sprite block or a diagnostics overlay.
    public static string WorstSubPass => Format(_worstSubName, _worstSubTicks);

    static string Format(string name, long ticks) => ticks == 0
        ? "none"
        : $"{name}:{ticks * 1000.0 / Stopwatch.Frequency:F1}ms";

    public static void BeginFrame()
    {
        _active = DiagnosticLogger.IsEnabled;
        if (!_active) return;

        _sw.Restart();
        _worstTicks = 0;
        _worstName = string.Empty;
        _worstSubTicks = 0;
        _worstSubName = string.Empty;
    }

    // Closes the named pass and starts timing the next one.
    public static void Pass(string name)
    {
        if (!_active) return;

        _sw.Stop();
        long elapsedTicks = _sw.ElapsedTicks;
        DiagnosticLogger.LogRenderPass(name, elapsedTicks);
        if (elapsedTicks > _worstTicks)
        {
            _worstTicks = elapsedTicks;
            _worstName = name;
        }
        _sw.Restart();
    }

    // Times a sub-segment of the pass being timed right now and logs it as its own row, without
    // breaking the pass chain: `world.asteroids` keeps its old total, and `asteroids.litDraws` plus
    // `asteroids.overlay` show where that total went. Used where one pass mixes work of very
    // different cost - the lit sprite blocks and the diagnostics collider overlay.
    public static void SubPass(string name, Action work)
    {
        if (!_active)
        {
            work();
            return;
        }

        _subSw.Restart();
        work();
        _subSw.Stop();

        long elapsedTicks = _subSw.ElapsedTicks;
        DiagnosticLogger.LogRenderPass(name, elapsedTicks);
        if (elapsedTicks > _worstSubTicks)
        {
            _worstSubTicks = elapsedTicks;
            _worstSubName = name;
        }
    }
}
