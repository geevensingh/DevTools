using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;

namespace DiffViewer.ViewModels;

/// <summary>
/// One row in the left-pane file list. Wraps a <see cref="FileChange"/> and
/// exposes the rendered path under the active <see cref="FileListDisplayMode"/>
/// plus the (possibly-still-unknown) "has visible differences" flag set by
/// the eager pre-diff pass.
/// </summary>
public sealed partial class FileEntryViewModel : ObservableObject
{
    public FileChange Change { get; }
    private readonly string _repoRoot;

    /// <summary>
    /// True / false once the pre-diff pass has scored this entry; <c>null</c>
    /// while still pending. Drives the <c>(whitespace-only)</c> grey-out.
    /// </summary>
    [ObservableProperty]
    private bool? _hasVisibleDifferences;

    /// <summary>Path string under the currently-active display mode; updated by FileListViewModel.</summary>
    [ObservableProperty]
    private string _displayPath = string.Empty;

    public FileEntryViewModel(FileChange change, string repoRoot)
    {
        Change = change ?? throw new ArgumentNullException(nameof(change));
        _repoRoot = repoRoot ?? throw new ArgumentNullException(nameof(repoRoot));
        DisplayPath = RepoRelativePath;
    }

    /// <summary>Repo-relative path, always with backslashes on Windows for display.</summary>
    public string RepoRelativePath => Change.Path.Replace('/', Path.DirectorySeparatorChar);

    /// <summary>Absolute path (computed from repo root + the relative path).</summary>
    public string FullPath => Path.Combine(_repoRoot, RepoRelativePath);

    /// <summary>Final filename (basename) only.</summary>
    public string FileName => Path.GetFileName(RepoRelativePath);

    /// <summary>Repo-relative directory of the entry; empty for repo-root files.</summary>
    public string DirectoryPath => Path.GetDirectoryName(RepoRelativePath) ?? string.Empty;

    // ---- Context-menu visibility predicates ----
    // These drive MenuItem.Visibility via BoolToVisibilityConverter so the
    // View doesn't need any code-behind branching.

    /// <summary>True iff the file actually exists on disk right now.</summary>
    public bool ExistsOnDisk => File.Exists(FullPath);

    /// <summary>
    /// True for staged-section rows whose index blob differs from the
    /// working-tree file content. Per the plan, *Open in external editor*
    /// is hidden in that case (the user might think they're opening the
    /// staged version when they'd actually open the working-tree version).
    /// </summary>
    public bool ShouldHideOpenInExternalEditor =>
        Change.Layer == WorkingTreeLayer.Staged && IndexBlobDiffersFromDisk();

    private bool IndexBlobDiffersFromDisk()
    {
        if (!ExistsOnDisk) return true;
        if (Change.RightBlobSha is null) return false;
        try
        {
            // Quick file-size sanity first (cheap; right size matches index
            // blob size most of the time on legit staged-then-edited rows).
            var info = new FileInfo(FullPath);
            return Change.RightFileSizeBytes is { } indexSize && info.Length != indexSize;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True for Untracked-layer rows - <c>git add</c> applies.</summary>
    public bool IsUntracked => Change.Layer == WorkingTreeLayer.Untracked;

    /// <summary>True when <em>Copy diff (unified)</em> is meaningful.</summary>
    public bool CanCopyDiffAsUnified => !Change.IsBinary && !Change.IsLfsPointer;

    /// <summary>True when *Copy blob SHA (left)* should appear (left side is a real committed blob).</summary>
    public bool HasLeftBlobSha => !string.IsNullOrEmpty(Change.LeftBlobSha);

    /// <summary>True when *Copy blob SHA (right)* should appear.</summary>
    public bool HasRightBlobSha => !string.IsNullOrEmpty(Change.RightBlobSha);

    /// <summary>Re-render <see cref="DisplayPath"/> for the supplied display mode.</summary>
    public void ApplyDisplayMode(FileListDisplayMode mode)
    {
        DisplayPath = mode switch
        {
            FileListDisplayMode.FullPath => FullPath,
            FileListDisplayMode.RepoRelative => RepoRelativePath,
            FileListDisplayMode.GroupedByDirectory => FileName,
            _ => RepoRelativePath,
        };
    }

    /// <summary>Single-character status badge: A / D / M / R / C / T / U / ?? / SM.</summary>
    public string StatusBadge => Change.Status switch
    {
        Models.FileStatus.Added => "A",
        Models.FileStatus.Deleted => "D",
        Models.FileStatus.Modified => "M",
        Models.FileStatus.Renamed => "R",
        Models.FileStatus.Copied => "C",
        Models.FileStatus.TypeChanged => "T",
        Models.FileStatus.Untracked => "??",
        Models.FileStatus.Conflicted => Change.ConflictCode ?? "UU",
        Models.FileStatus.SubmoduleMoved => "SM",
        _ => "?",
    };

    /// <summary>For renames, the rendered "old → new" suffix; otherwise empty.</summary>
    public string RenameDescriptor =>
        Change.IsRenameOrCopy && Change.OldPath is not null
            ? $" (was {Change.OldPath.Replace('/', Path.DirectorySeparatorChar)})"
            : string.Empty;

    /// <summary>True once the pre-diff pass has flagged this entry as whitespace-only.</summary>
    public bool IsWhitespaceOnly => HasVisibleDifferences == false;

    partial void OnHasVisibleDifferencesChanged(bool? value)
    {
        OnPropertyChanged(nameof(IsWhitespaceOnly));
    }
}
