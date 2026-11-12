using System.Text;
using Spacevors.Domain;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;

namespace Spacevors.Game;

// What the enemy manœuvres actually delivered in this run (plans/ENEMY_BEHAVIORS.md follow-up): how many
// hulls of each behaviour appeared, and how many seconds they spent alive and on screen. One line per 10 s
// of play plus one total line at the end, so a ten-minute session leaves about sixty rows instead of a row
// per hull per frame. Diagnostics mode only.
//
// Why it exists: the armament ladder is arithmetic, so it says what share of spawns is *dealt* a behaviour —
// which is not what the player meets. A behaviour dealt at 10 % of spawns and killed within two seconds is
// not a formation, and timing the hulls is the only way to tell a knob that works from one that only moves
// a table.
static class BehaviourLog
{
    // Hull-seconds integrate at 4 Hz: a few hundred hull reads a second, invisible next to a frame, and
    // fine-grained enough that a hull which lived half a second is not rounded away.
    const float SampleInterval = 0.25f;

    // One row per 10 s of play. Every second would be a flood; a minute would hide which minute the flanks
    // first showed up in.
    const float WindowSeconds = 10f;

    // And the running totals repeated once a minute, so a run that never reaches Close() still leaves an
    // answer within a minute of where it stopped.
    const float TotalSeconds = 60f;

    const string DirEnvVar = "SPACEVORS_BEHAVIOUR_DIR";
    static readonly TimeSpan KeepFor = TimeSpan.FromDays(1);

    static BehaviourTally _tally = new();
    static readonly EnemyBehavior[] _behaviors = Enum.GetValues<EnemyBehavior>();

    // Totals as of the last printed row, so each row is the delta since the previous one.
    static readonly float[] _printedAlive = new float[BehaviourTally.BehaviorCount];
    static readonly float[] _printedOnScreen = new float[BehaviourTally.BehaviorCount];
    static readonly int[] _printedSeen = new int[BehaviourTally.BehaviorCount];

    static StreamWriter? _file;
    static float _nextSampleAt, _nextRowAt, _nextTotalAt, _lastElapsed;

    public static bool IsActive => _file != null;

    public static void Open()
    {
        if (!DiagnosticLogger.IsEnabled) return;

        _file = DiagnosticsFile.Open(DirEnvVar, "behaviours", KeepFor, out string? path);
        if (_file == null) return;

        Console.WriteLine(
            $"[BEHAVIOUR] log: {path} sample={SampleInterval:F2}s window={WindowSeconds:F0}s " +
            $"totals every={TotalSeconds:F0}s keepDays={KeepFor.TotalDays:F0}");
    }

    // A new GameSession is a new run: the tally and the row deltas start again, so a second run out of the
    // same process never reports the first one's hulls.
    public static void StartRun()
    {
        _tally = new BehaviourTally();
        Array.Clear(_printedAlive);
        Array.Clear(_printedOnScreen);
        Array.Clear(_printedSeen);
        _nextSampleAt = _nextRowAt = 0f;
        _nextTotalAt = TotalSeconds;
        _lastElapsed = 0f;
    }

    // Called every frame; the tally keeps its own clock, so the seconds it reports are game seconds whether
    // the run is at 120 fps or stalled at 4.
    public static void Sample(WorldView view)
    {
        if (_file == null) return;

        float now = view.ElapsedTime;
        if (now < _nextSampleAt) return;

        _nextSampleAt = now + SampleInterval;
        _lastElapsed = now;
        _tally.Sample(view);

        if (now < _nextRowAt) return;

        _nextRowAt = now + WindowSeconds;
        WriteRow(now);

        // The totals are repeated once a minute as well as at the end: a run that is killed, or a window
        // closed by a script, never reaches Close(), and deltas alone cannot be summed into an answer.
        if (now >= _nextTotalAt)
        {
            _nextTotalAt = now + TotalSeconds;
            _file.WriteLine(TotalsLine("[BEHAVIOUR-TOTAL]"));
        }
    }

    public static void Close()
    {
        if (_file == null) return;

        string line = TotalsLine("[BEHAVIOUR-END]");
        Console.WriteLine(line);
        _file.WriteLine(line);

        _file.Dispose();
        _file = null;
    }

    // The run's answer in one line. It lists every behaviour — including the ones that never happened — so a
    // zero is a measurement rather than an omission.
    static string TotalsLine(string label)
    {
        var line = new StringBuilder($"{label} t={_lastElapsed:F0}s");
        for (int i = 0; i < _behaviors.Length; i++)
            line.Append($" {_behaviors[i]} hulls={_tally.Seen[i]} alive={_tally.AliveSeconds[i]:F1}s" +
                        $" onscreen={_tally.OnScreenSeconds[i]:F1}s peak={_tally.PeakLive[i]:F0};");
        return line.ToString();
    }

    // One window of the run: which behaviours were on screen, how many new hulls arrived, and how long the
    // ones that were there stayed. A behaviour that did nothing in this window says nothing in this row;
    // a row that ends in `-` is the log proving it is still alive during a lull.
    static void WriteRow(float now)
    {
        var line = new StringBuilder($"[BEHAVIOUR] t={now:F0}s win={WindowSeconds:F0}s");
        int columnsBefore = line.Length;

        for (int i = 0; i < _behaviors.Length; i++)
        {
            float alive = _tally.AliveSeconds[i] - _printedAlive[i];
            float onScreen = _tally.OnScreenSeconds[i] - _printedOnScreen[i];
            int seen = _tally.Seen[i] - _printedSeen[i];

            _printedAlive[i] = _tally.AliveSeconds[i];
            _printedOnScreen[i] = _tally.OnScreenSeconds[i];
            _printedSeen[i] = _tally.Seen[i];

            if (seen == 0 && alive <= 0f) continue;
            line.Append($" {_behaviors[i]} +{seen} hulls {alive:F1}s alive {onScreen:F1}s on-screen;");
        }

        if (line.Length == columnsBefore) line.Append(" none");
        _file!.WriteLine(line.ToString());
    }
}
