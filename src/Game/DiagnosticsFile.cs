using Spacevors.Domain;

namespace Spacevors.Game;

// One diagnostics file per run: `<prefix>-<utc timestamp>Z-p<pid>.log`, in a directory the player can
// override with an env var, pruned of this project's older files before the run starts. AutoFlush, so a
// session that ends in a crash keeps what it wrote.
//
// The default directory is inside the project - next to the running binary, under gitignored bin/ - rather
// than a temp directory: a temp dir is invisible to the tooling and to whoever asks "what did my run leave
// behind", and it is cleared on reboot. A read-only install (an AppImage) cannot write beside its binary,
// so it points the env var at something writable; there, or for any other failure, the log warns once and
// the run goes on without it.
static class DiagnosticsFile
{
    public static StreamWriter? Open(string dirEnvVar, string prefix, TimeSpan keepFor, out string? path)
    {
        path = null;
        string dir = Environment.GetEnvironmentVariable(dirEnvVar)
            ?? Path.Combine(AppContext.BaseDirectory, prefix);
        try
        {
            Directory.CreateDirectory(dir);
            PruneOldFiles(dir, prefix, keepFor);

            // The pid, so two runs started in the same second cannot clobber each other.
            path = Path.Combine(dir, $"{prefix}-{DateTime.UtcNow:yyyyMMddTHHmmss}Z-p{Environment.ProcessId}.log");
            return new StreamWriter(path) { AutoFlush = true };
        }
        catch (Exception e)
        {
            DiagnosticLogger.LogWarning($"{prefix} log disabled: {e.Message}");
            path = null;
            return null;
        }
    }

    static void PruneOldFiles(string dir, string prefix, TimeSpan keepFor)
    {
        DateTime cutoff = DateTime.UtcNow - keepFor;
        foreach (string file in Directory.GetFiles(dir, $"{prefix}-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
            }
            catch (IOException)
            {
                // A file another run still holds stays; the next open tries it again.
            }
        }
    }
}
