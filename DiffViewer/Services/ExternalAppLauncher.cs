using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Production implementation of <see cref="IExternalAppLauncher"/>.
///
/// <para><b>Detection order:</b> <c>where.exe &lt;name&gt;</c> first
/// (covers the case where the user has Code on PATH), then well-known
/// LOCALAPPDATA + ProgramFiles probes for the five members of the
/// VS Code family — VS Code, VS Code Insiders, VSCodium, Cursor,
/// Windsurf — in that priority order. All five share the
/// <c>--goto &lt;path&gt;:&lt;line&gt;</c> arg shape, so the launch
/// path is identical once one is resolved.</para>
///
/// <para><b>Settings override:</b> if the user has set
/// <see cref="AppSettings.ExternalEditorPath"/>, that path takes priority
/// over auto-detect. If that file no longer exists at launch time we
/// quietly fall back to auto-detect rather than refusing to open.</para>
/// </summary>
public sealed class ExternalAppLauncher : IExternalAppLauncher
{
    private readonly ISettingsService? _settings;
    private readonly IEditorProbe _probe;
    private EditorResolution? _cached;
    private readonly object _gate = new();

    public ExternalAppLauncher(ISettingsService? settings = null, IEditorProbe? probe = null)
    {
        _settings = settings;
        _probe = probe ?? new WhereExeEditorProbe();
    }

    public EditorResolution ResolveEditor(bool forceReDetect = false)
    {
        lock (_gate)
        {
            if (!forceReDetect && _cached is not null) return _cached;

            // 1. User-configured path wins (when it actually exists).
            var configured = _settings?.Current.ExternalEditorPath;
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            {
                _cached = new EditorResolution(
                    configured,
                    GuessFamilyFromPath(configured),
                    _settings?.Current.ExternalEditorLineArgFormat);
                return _cached;
            }

            // 2. PATH probe via where.exe, in family priority order.
            foreach (var name in EditorBinNames)
            {
                var resolved = _probe.TryResolveOnPath(name);
                if (resolved is not null)
                {
                    _cached = new EditorResolution(resolved, EditorFamily.VsCodeFamily, "--goto {path}:{line}");
                    return _cached;
                }
            }

            // 3. Well-known install paths (LOCALAPPDATA then ProgramFiles).
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (var (relative, _) in WellKnownInstallSubpaths)
            {
                foreach (var root in new[] { Path.Combine(local, "Programs"), programFiles })
                {
                    var candidate = Path.Combine(root, relative);
                    if (File.Exists(candidate))
                    {
                        _cached = new EditorResolution(candidate, EditorFamily.VsCodeFamily, "--goto {path}:{line}");
                        return _cached;
                    }
                }
            }

            // 4. Nothing found.
            _cached = new EditorResolution(null, EditorFamily.None, null);
            return _cached;
        }
    }

    public async Task<LaunchResult> LaunchEditorAsync(string filePath, int line = 0)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return LaunchResult.Fail("File path is empty.");

        // Tripwire from the plan: VS Code parses the LAST `:` as line
        // number, which is correct on Windows because NTFS bans `:` in
        // filenames. UNC / Linux paths or future bugs should be caught
        // here in Debug.
        var afterDrive = filePath.Length >= 3 && filePath[1] == ':' ? filePath.Substring(2) : filePath;
        Debug.Assert(!afterDrive.Contains(':'),
            "Path contains a colon past the drive letter; VS Code's --goto would mis-parse it.");

        var resolved = ResolveEditor();
        if (resolved.ExecutablePath is null)
        {
            return await LaunchOsDefaultAsync(filePath).ConfigureAwait(false);
        }

        try
        {
            var psi = BuildEditorProcessStart(resolved, filePath, line);
            using var p = Process.Start(psi);
            return p is null
                ? LaunchResult.Fail($"Failed to start {resolved.ExecutablePath}")
                : LaunchResult.Ok();
        }
        catch (Exception ex)
        {
            return LaunchResult.Fail($"Editor launch failed: {ex.Message}");
        }
    }

    public LaunchResult ShowInExplorer(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return LaunchResult.Fail("File path is empty.");
        try
        {
            var psi = new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true,
            };
            // /select, must be a single concatenated arg with the path,
            // and the path must be quoted manually because of the embedded
            // comma.
            psi.Arguments = $"/select,\"{filePath}\"";
            using var p = Process.Start(psi);
            return p is null ? LaunchResult.Fail("explorer.exe failed to start.") : LaunchResult.Ok();
        }
        catch (Exception ex)
        {
            return LaunchResult.Fail($"Show-in-Explorer failed: {ex.Message}");
        }
    }

    public LaunchResult OpenWithDefaultApp(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return LaunchResult.Fail("File path is empty.");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true,
            };
            using var p = Process.Start(psi);
            return p is null ? LaunchResult.Fail("OS-default open failed.") : LaunchResult.Ok();
        }
        catch (Exception ex)
        {
            return LaunchResult.Fail($"OS-default open failed: {ex.Message}");
        }
    }

    private async Task<LaunchResult> LaunchOsDefaultAsync(string filePath)
    {
        await Task.Yield();
        return OpenWithDefaultApp(filePath);
    }

    /// <summary>
    /// Build a <see cref="ProcessStartInfo"/> for <paramref name="resolved"/>.
    /// Picks <c>UseShellExecute</c> by file extension — <c>.cmd</c>/<c>.bat</c>
    /// shims (the typical <c>code.cmd</c> install) need shell-execute=true so
    /// Windows follows PATHEXT; real <c>.exe</c> editors get shell-execute=false
    /// so we can wire stdin/stdout if ever needed.
    /// </summary>
    internal static ProcessStartInfo BuildEditorProcessStart(EditorResolution resolved, string filePath, int line)
    {
        var ext = Path.GetExtension(resolved.ExecutablePath!).ToLowerInvariant();
        var isShim = ext is ".cmd" or ".bat";

        var psi = new ProcessStartInfo
        {
            FileName = resolved.ExecutablePath!,
            UseShellExecute = isShim,
            CreateNoWindow = isShim,
            WindowStyle = isShim ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
        };

        var format = resolved.LineArgFormat ?? "--goto {path}:{line}";
        AppendArgsFromFormat(psi, format, filePath, line);
        return psi;
    }

    /// <summary>
    /// Convert a <c>"--goto {path}:{line}"</c>-style format into individual
    /// ArgumentList entries. We split on whitespace OUTSIDE braces so the
    /// path-and-line "{path}:{line}" stays one arg even though it has
    /// punctuation. The format string is the only template substitution
    /// we do — never <c>cmd /c</c> concatenation.
    /// </summary>
    internal static void AppendArgsFromFormat(ProcessStartInfo psi, string format, string filePath, int line)
    {
        var formatHasLineToken = format.Contains("{line}");
        foreach (var token in format.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Contains("{path}") || token.Contains("{line}"))
            {
                if (line <= 0 && token == "{path}:{line}")
                {
                    psi.ArgumentList.Add(filePath);
                    continue;
                }
                if (line <= 0 && token.Contains("{line}"))
                {
                    // Bare {line}-only token without path - drop entirely
                    // when no line was requested.
                    continue;
                }
                psi.ArgumentList.Add(token
                    .Replace("{path}", filePath)
                    .Replace("{line}", line.ToString()));
            }
            else
            {
                // Drop --goto (and similar line-affixing flags) when no
                // line was requested AND the format references {line}.
                if (line <= 0 && formatHasLineToken && token == "--goto") continue;
                psi.ArgumentList.Add(token);
            }
        }
    }

    private static EditorFamily GuessFamilyFromPath(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        return name is "code" or "code-insiders" or "codium" or "cursor" or "windsurf"
            ? EditorFamily.VsCodeFamily
            : EditorFamily.Custom;
    }

    private static readonly string[] EditorBinNames =
    {
        "code", "code-insiders", "codium", "cursor", "windsurf",
    };

    private static readonly (string RelativePath, string Family)[] WellKnownInstallSubpaths =
    {
        (@"Microsoft VS Code\bin\code.cmd", "code"),
        (@"Microsoft VS Code Insiders\bin\code-insiders.cmd", "code-insiders"),
        (@"VSCodium\bin\codium.cmd", "codium"),
        (@"Cursor\bin\cursor.cmd", "cursor"),
        (@"Windsurf\bin\windsurf.cmd", "windsurf"),
    };
}

/// <summary>
/// Minimal abstraction over the <c>where.exe</c> shell-out used to
/// detect editors. Tests substitute a fake to avoid relying on the
/// dev machine's installed editors.
/// </summary>
public interface IEditorProbe
{
    /// <summary>
    /// Return the absolute path of <paramref name="binaryName"/> if it's
    /// on PATH; <c>null</c> otherwise.
    /// </summary>
    string? TryResolveOnPath(string binaryName);
}

internal sealed class WhereExeEditorProbe : IEditorProbe
{
    public string? TryResolveOnPath(string binaryName)
    {
        try
        {
            var psi = new ProcessStartInfo("where.exe", binaryName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            if (p.ExitCode != 0) return null;
            // where.exe returns one line per match; first line wins.
            var firstLine = output.Split('\n', '\r')[0].Trim();
            return string.IsNullOrEmpty(firstLine) || !File.Exists(firstLine) ? null : firstLine;
        }
        catch
        {
            return null;
        }
    }
}
