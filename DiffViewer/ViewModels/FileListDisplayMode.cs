namespace DiffViewer.ViewModels;

/// <summary>
/// Mutually-exclusive display modes for paths in the left-pane file list.
/// </summary>
public enum FileListDisplayMode
{
    /// <summary><c>C:\Repos\foo\src\bar\baz.cs</c></summary>
    FullPath,

    /// <summary><c>src\bar\baz.cs</c></summary>
    RepoRelative,

    /// <summary>Tree view: directory nodes contain file leaves.</summary>
    GroupedByDirectory,
}
