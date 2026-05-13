using System.Threading.Tasks;

namespace DiffViewer.Services;

/// <summary>
/// Launches external applications on behalf of the user — VS Code-family
/// editors at a specific line, Explorer's "show file in folder" view, the
/// OS-default handler for a file. All three are invoked from the file-list
/// and diff-pane context menus.
///
/// <para>Editor detection is cached after first probe (see
/// <see cref="ResolveEditor"/>). Tests can pass a fake
/// <see cref="EditorProbe"/> to keep them deterministic — the production
/// probe shells out to <c>where.exe</c>.</para>
/// </summary>
public interface IExternalAppLauncher
{
    /// <summary>
    /// Open <paramref name="filePath"/> at <paramref name="line"/> in the
    /// configured (or auto-detected) external editor. Falls back to the OS
    /// shell-default if no VS Code-family editor is configured.
    /// </summary>
    /// <param name="line">1-based; pass 0 to open without jumping.</param>
    Task<LaunchResult> LaunchEditorAsync(string filePath, int line = 0);

    /// <summary>
    /// Open Windows Explorer with <paramref name="filePath"/> selected.
    /// Equivalent to <c>explorer.exe /select,&lt;filePath&gt;</c>.
    /// </summary>
    LaunchResult ShowInExplorer(string filePath);

    /// <summary>
    /// Open <paramref name="filePath"/> with the OS-default handler
    /// (whatever the user has registered for the extension).
    /// </summary>
    LaunchResult OpenWithDefaultApp(string filePath);

    /// <summary>
    /// Resolve the editor that <see cref="LaunchEditorAsync"/> would use,
    /// without launching it. Returns the executable path + the family the
    /// detection picked. Cached after first call.
    /// </summary>
    EditorResolution ResolveEditor(bool forceReDetect = false);
}

/// <summary>Outcome of a single launcher call.</summary>
public sealed record LaunchResult(bool Success, string? ErrorMessage)
{
    public static LaunchResult Ok() => new(true, null);
    public static LaunchResult Fail(string message) => new(false, message);
}

/// <summary>
/// Detected (or configured) external editor. <see cref="ExecutablePath"/>
/// is null when no editor could be resolved — callers should fall through
/// to <c>start &lt;path&gt;</c>.
/// </summary>
public sealed record EditorResolution(
    string? ExecutablePath,
    EditorFamily Family,
    string? LineArgFormat);

public enum EditorFamily
{
    /// <summary>No editor resolved; fall through to OS-default shell-open.</summary>
    None,
    /// <summary>VS Code or any fork that inherits its arg shape.</summary>
    VsCodeFamily,
    /// <summary>User-configured editor of unknown family — invoked verbatim.</summary>
    Custom,
}
