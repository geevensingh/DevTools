using System.Diagnostics;
using System.IO;

namespace CopilotSessionMonitor.Core;

/// <summary>
/// Resolves the per-session lock file <c>inuse.&lt;PID&gt;.lock</c>: detects its
/// presence and confirms the named PID is actually still alive.
/// </summary>
public static class PidLiveness
{
    /// <summary>Update <paramref name="state"/>'s LockFilePresent / LockPid / PidAlive based on the session folder.</summary>
    public static void Probe(SessionState state)
    {
        state.LockFilePresent = false;
        state.LockPid = null;
        state.PidAlive = false;

        string? lockFile = null;
        try
        {
            // Filename pattern: inuse.<PID>.lock
            foreach (var f in Directory.EnumerateFiles(state.SessionDirectory, "inuse.*.lock"))
            {
                lockFile = f;
                break;
            }
        }
        catch (DirectoryNotFoundException) { return; }
        catch (UnauthorizedAccessException) { return; }

        if (lockFile is null) return;
        state.LockFilePresent = true;

        // Parse "inuse.32500.lock" -> 32500
        var name = Path.GetFileName(lockFile);
        var parts = name.Split('.');
        if (parts.Length >= 3 && int.TryParse(parts[1], out var pid))
        {
            state.LockPid = pid;
            state.PidAlive = IsPidAlive(pid);
        }
    }

    public static bool IsPidAlive(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            // GetProcessById throws ArgumentException for unknown PIDs; if we got
            // here the OS process record exists. HasExited can race with cleanup
            // but is the most authoritative signal we have without P/Invoke.
            return !p.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return true; }
    }
}
