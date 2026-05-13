using DiffViewer.Models;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

public class FileEntryViewModelTests
{
    private static FileChange Modified(string path, string? oldPath = null) =>
        new(
            Path: path,
            OldPath: oldPath,
            Status: oldPath is null ? Models.FileStatus.Modified : Models.FileStatus.Renamed,
            ConflictCode: null,
            Layer: WorkingTreeLayer.Unstaged,
            LeftBlobSha: null, RightBlobSha: null,
            IsBinary: false,
            LeftFileSizeBytes: null, RightFileSizeBytes: null,
            IsLfsPointer: false, IsSparseNotCheckedOut: false,
            OldMode: 0, NewMode: 0);

    [Fact]
    public void Paths_Are_BackslashSeparated_OnWindows()
    {
        var e = new FileEntryViewModel(Modified("src/foo/bar.cs"), @"C:\repo");

        e.RepoRelativePath.Should().Be(@"src\foo\bar.cs");
        e.FullPath.Should().Be(@"C:\repo\src\foo\bar.cs");
        e.FileName.Should().Be("bar.cs");
        e.DirectoryPath.Should().Be(@"src\foo");
    }

    [Fact]
    public void ApplyDisplayMode_SwitchesDisplayPath()
    {
        var e = new FileEntryViewModel(Modified("src/foo.cs"), @"C:\repo");

        e.ApplyDisplayMode(FileListDisplayMode.FullPath);
        e.DisplayPath.Should().Be(@"C:\repo\src\foo.cs");

        e.ApplyDisplayMode(FileListDisplayMode.RepoRelative);
        e.DisplayPath.Should().Be(@"src\foo.cs");

        e.ApplyDisplayMode(FileListDisplayMode.GroupedByDirectory);
        e.DisplayPath.Should().Be("foo.cs");
    }

    [Fact]
    public void RenameDescriptor_PopulatedForRenames_EmptyOtherwise()
    {
        var renamed = new FileEntryViewModel(Modified("new.cs", oldPath: "old.cs"), @"C:\repo");
        renamed.RenameDescriptor.Should().Contain("old.cs");

        var modified = new FileEntryViewModel(Modified("a.cs"), @"C:\repo");
        modified.RenameDescriptor.Should().BeEmpty();
    }

    [Fact]
    public void IsWhitespaceOnly_TracksHasVisibleDifferencesNegation()
    {
        var e = new FileEntryViewModel(Modified("a.cs"), @"C:\repo");
        e.IsWhitespaceOnly.Should().BeFalse(); // null == not yet known, treated as not flagged

        e.HasVisibleDifferences = true;
        e.IsWhitespaceOnly.Should().BeFalse();

        e.HasVisibleDifferences = false;
        e.IsWhitespaceOnly.Should().BeTrue();
    }
}
