namespace Spacevors.Domain;

public static class DiagnosticLogger
{
    private static readonly bool _enabled = Environment.GetEnvironmentVariable("SPACEVORS_DIAGNOSTIC") == "1";
    private static readonly object _lock = new();

    private static int _frameCount;
    private static double _fpsTimer;
    private static int _currentFps;

    public static void UpdateFps(float frameTime)
    {
        if (!_enabled) return;

        _frameCount++;
        _fpsTimer += frameTime;
        if (_fpsTimer >= 1.0)
        {
            _currentFps = (int)(_frameCount / _fpsTimer);
            lock (_lock)
            {
                Console.WriteLine($"[FPS] {_currentFps}");
            }
            _frameCount = 0;
            _fpsTimer = 0.0;
        }
    }

    public static void LogSystem(string systemName, long elapsedTicks, int entitiesUpdated = 0)
    {
        if (!_enabled) return;

        long ms = elapsedTicks / TimeSpan.TicksPerMillisecond;
        lock (_lock)
        {
            if (entitiesUpdated > 0)
                Console.WriteLine($"[SYSTEM] {systemName}: {ms}ms ({entitiesUpdated} entities)");
            else
                Console.WriteLine($"[SYSTEM] {systemName}: {ms}ms");
        }
    }

    public static void LogWarning(string message)
    {
        if (!_enabled) return;

        lock (_lock)
        {
            Console.Error.WriteLine($"[WARNING] {message}");
        }
    }

    public static void LogMouse(int x, int y, bool leftDown, bool rightDown, bool middleDown)
    {
        if (!_enabled) return;

        lock (_lock)
        {
            Console.WriteLine($"[MOUSE] X:{x} Y:{y} L:{leftDown} R:{rightDown} M:{middleDown}");
        }
    }



    public static void LogAllEnemyShips(string label, IReadOnlyList<(string Name, Vector2 Position)> ships)
    {
        if (!_enabled) return;

        lock (_lock)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"[SHIPS:{label}] count={ships.Count} ");
            foreach (var (name, pos) in ships)
            {
                sb.Append($"{name}=({pos.X:F1},{pos.Y:F1}) ");
            }
            Console.WriteLine(sb.ToString());
        }
    }

    public static void LogFrameStart()
    {
        if (!_enabled) return;

        lock (_lock)
        {
            Console.WriteLine("[FRAME]");
        }
    }
}
