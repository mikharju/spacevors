namespace Spacevors.Domain;

public static class DiagnosticLogger
{
    private static readonly bool _enabled = Environment.GetEnvironmentVariable("SPACEVORS_DIAGNOSTIC") == "1";
    private static readonly object _lock = new();

    public static void LogSystem(string systemName, long elapsedTicks)
    {
        if (!_enabled) return;

        long ms = elapsedTicks / TimeSpan.TicksPerMillisecond;
        lock (_lock)
        {
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
}
