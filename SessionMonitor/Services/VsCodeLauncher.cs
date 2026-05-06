using System.Diagnostics;
using System.IO;

namespace CopilotSessionMonitor.Services;

/// <summary>Open a folder in VS Code via the <c>code</c> shim on PATH.</summary>
public static class VsCodeLauncher
{
    public static bool TryOpen(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
        return TryRun("code", path) || TryRun("code-insiders", path);
    }

    private static bool TryRun(string exe, string arg)
    {
        try
        {
            // Use the shell so the .cmd shim (e.g. C:\...\Microsoft VS Code\bin\code.cmd) resolves on PATH.
            var psi = new ProcessStartInfo(exe, $"\"{arg}\"")
            {
                UseShellExecute = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            return p is not null;
        }
        catch { return false; }
    }
}
