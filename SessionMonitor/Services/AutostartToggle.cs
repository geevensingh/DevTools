using System.IO;

namespace CopilotSessionMonitor.Services;

/// <summary>
/// Toggles a shortcut in the user's Startup folder so the app launches at
/// logon. We use the COM IWshShell to write a real .lnk so it survives moves
/// of the .exe (it'll just stop working — the user can re-toggle).
/// Note: we use COM through dynamic to avoid embedding the IWshRuntimeLibrary
/// PIA at build time. Falls back to no-op if anything fails.
/// </summary>
public static class AutostartToggle
{
    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "Copilot Session Monitor.lnk");

    public static bool IsEnabled => File.Exists(ShortcutPath);

    public static bool Toggle()
    {
        try
        {
            if (IsEnabled)
            {
                File.Delete(ShortcutPath);
                return false;
            }
            CreateShortcut(ShortcutPath, GetCurrentExe());
            return true;
        }
        catch
        {
            return IsEnabled;
        }
    }

    private static string GetCurrentExe() =>
        System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
        ?? Path.Combine(AppContext.BaseDirectory, "CopilotSessionMonitor.exe");

    private static void CreateShortcut(string lnkPath, string targetExe)
    {
        // Use COM IWshShell via late binding (no PIA reference required).
        var t = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("WScript.Shell unavailable");
        dynamic shell = Activator.CreateInstance(t)!;
        dynamic shortcut = shell.CreateShortcut(lnkPath);
        shortcut.TargetPath = targetExe;
        shortcut.WorkingDirectory = Path.GetDirectoryName(targetExe) ?? "";
        shortcut.IconLocation = targetExe + ",0";
        shortcut.Description = "Copilot Session Monitor";
        shortcut.Save();
    }
}
