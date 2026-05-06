using System.IO;

namespace CopilotSessionMonitor;

/// <summary>
/// Cheap append-only log to <c>%LOCALAPPDATA%\CopilotSessionMonitor\app.log</c>.
/// Used to capture startup errors and unhandled exceptions, since this app
/// runs detached from any console and would otherwise fail silently.
/// Rotated when the file exceeds <see cref="MaxBytes"/> (current →
/// <c>app.log.old</c>); only one rotated file is kept.
/// </summary>
internal static class DebugLog
{
    private const long MaxBytes = 10 * 1024 * 1024; // 10 MB
    private static readonly object s_gate = new();
    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CopilotSessionMonitor",
        "app.log");

    private static string OldPath => Path + ".old";

    static DebugLog()
    {
        try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!); } catch { /* ignore */ }
    }

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? ex = null)
    {
        var msg = ex is null ? message : $"{message}: {ex}";
        Write("ERROR", msg);
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (s_gate)
            {
                RotateIfNeeded();
                File.AppendAllText(Path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never throw */ }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(Path)) return;
            var len = new FileInfo(Path).Length;
            if (len < MaxBytes) return;

            // Replace any prior rotated copy with the current one, then start fresh.
            try { if (File.Exists(OldPath)) File.Delete(OldPath); } catch { /* ignore */ }
            try { File.Move(Path, OldPath); } catch { /* if Move fails, fall back to truncating */ File.WriteAllText(Path, ""); }
        }
        catch
        {
            // Rotation failures must not stop logging; leave the file as-is.
        }
    }
}
