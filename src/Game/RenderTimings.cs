using System.Diagnostics;
using Spacevors.Domain;

namespace Spacevors.Game;

// Per-pass render timing for the perf scenarios. One reused Stopwatch, pass names are literals, and
// every call is a single branch when diagnostics are off - measuring must not change what is measured.
// The pass named "present" covers EndDrawing, which is also where raylib's SetTargetFPS wait happens.
static class RenderTimings
{
    static readonly Stopwatch _sw = new();
    static bool _active;

    public static void BeginFrame()
    {
        _active = DiagnosticLogger.IsEnabled;
        if (_active) _sw.Restart();
    }

    // Closes the named pass and starts timing the next one.
    public static void Pass(string name)
    {
        if (!_active) return;

        _sw.Stop();
        DiagnosticLogger.LogRenderPass(name, _sw.ElapsedTicks);
        _sw.Restart();
    }
}
