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
