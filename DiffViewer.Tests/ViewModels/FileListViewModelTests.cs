using DiffViewer.Models;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

public class FileListViewModelTests
{
    private static FileChange MakeChange(
        string path,
        Models.FileStatus status = Models.FileStatus.Modified,
        WorkingTreeLayer layer = WorkingTreeLayer.Unstaged,
        string? oldPath = null) =>
        new(
            Path: path,
            OldPath: oldPath,
            Status: status,
            ConflictCode: status == Models.FileStatus.Conflicted ? "UU" : null,
            Layer: layer,
            LeftBlobSha: null,
            RightBlobSha: null,
            IsBinary: false,
            LeftFileSizeBytes: null,
            RightFileSizeBytes: null,
            IsLfsPointer: false,
            IsSparseNotCheckedOut: false,
            OldMode: 0,
            NewMode: 0);

    [Fact]
    public void LoadFromChanges_FlatList_WhenCommitVsCommit()
    {
        var vm = new FileListViewModel();
        var changes = new[]
        {
            MakeChange("src/a.cs"),
            MakeChange("src/b.cs"),
        };

        vm.LoadFromChanges(changes, @"C:\repo", isCommitVsCommit: true);

        vm.IsFlatLayout.Should().BeTrue();
        vm.Sections.Should().HaveCount(1);
        vm.Sections[0].Header.Should().Be("Changes");
        vm.Sections[0].Entries.Should().HaveCount(2);
        vm.FlatEntries.Should().HaveCount(2);
    }

    [Fact]
    public void LoadFromChanges_GroupsByLayer_InCanonicalOrder()
    {
        var vm = new FileListViewModel();
        var changes = new[]
        {
            MakeChange("u.txt", Models.FileStatus.Untracked, WorkingTreeLayer.Untracked),
            MakeChange("s.cs", layer: WorkingTreeLayer.Staged),
            MakeChange("c.cs", Models.FileStatus.Conflicted, WorkingTreeLayer.Conflicted),
            MakeChange("w.cs", layer: WorkingTreeLayer.Unstaged),
            MakeChange("h.cs", layer: WorkingTreeLayer.CommittedSinceCommit),
        };

        vm.LoadFromChanges(changes, @"C:\repo", isCommitVsCommit: false);

        vm.IsFlatLayout.Should().BeFalse();
        vm.Sections.Select(s => s.Layer).Should().Equal(
            WorkingTreeLayer.Conflicted,
            WorkingTreeLayer.CommittedSinceCommit,
            WorkingTreeLayer.Staged,
            WorkingTreeLayer.Unstaged,
            WorkingTreeLayer.Untracked);
    }

    [Fact]
    public void LoadFromChanges_OmitsEmptySections()
    {
        var vm = new FileListViewModel();
        var changes = new[]
        {
            MakeChange("a.cs", layer: WorkingTreeLayer.Staged),
            MakeChange("b.cs", layer: WorkingTreeLayer.Staged),
        };

        vm.LoadFromChanges(changes, @"C:\repo", isCommitVsCommit: false);

        vm.Sections.Should().ContainSingle();
        vm.Sections[0].Layer.Should().Be(WorkingTreeLayer.Staged);
    }

    [Fact]
    public void LoadFromChanges_ResetsBetweenCalls()
    {
        var vm = new FileListViewModel();
        vm.LoadFromChanges(new[] { MakeChange("a.cs") }, @"C:\repo", isCommitVsCommit: false);
        vm.LoadFromChanges(Array.Empty<FileChange>(), @"C:\repo", isCommitVsCommit: false);

        vm.Sections.Should().BeEmpty();
        vm.FlatEntries.Should().BeEmpty();
    }

    [Fact]
    public void LoadFromChanges_PreservesSelection_WhenSameFileStillInList()
    {
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs"), MakeChange("src/b.cs") },
            @"C:\repo", isCommitVsCommit: false);
        vm.SelectedEntry = vm.FlatEntries.Single(e => e.Change.Path == "src/b.cs");

        // Same file list (e.g. a no-op file-system event). New VMs but the
        // selection should still resolve to "src/b.cs".
        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs"), MakeChange("src/b.cs") },
            @"C:\repo", isCommitVsCommit: false);

        vm.SelectedEntry.Should().NotBeNull();
        vm.SelectedEntry!.Change.Path.Should().Be("src/b.cs");
        // The instance must be one of the new entries, not a dangling old ref.
        vm.FlatEntries.Should().Contain(vm.SelectedEntry);
    }

    [Fact]
    public void LoadFromChanges_PreservesSelection_AcrossUnrelatedAddRemove()
    {
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs"), MakeChange("src/b.cs") },
            @"C:\repo", isCommitVsCommit: false);
        vm.SelectedEntry = vm.FlatEntries.Single(e => e.Change.Path == "src/b.cs");

        // a.cs goes away, c.cs appears, but b.cs is still selected.
        vm.LoadFromChanges(
            new[] { MakeChange("src/b.cs"), MakeChange("src/c.cs") },
            @"C:\repo", isCommitVsCommit: false);

        vm.SelectedEntry.Should().NotBeNull();
        vm.SelectedEntry!.Change.Path.Should().Be("src/b.cs");
    }

    [Fact]
    public void LoadFromChanges_ClearsSelection_WhenSelectedFileFallsOut()
    {
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs"), MakeChange("src/b.cs") },
            @"C:\repo", isCommitVsCommit: false);
        vm.SelectedEntry = vm.FlatEntries.Single(e => e.Change.Path == "src/b.cs");

        // b.cs no longer in the list (e.g. user reverted it).
        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs") },
            @"C:\repo", isCommitVsCommit: false);

        vm.SelectedEntry.Should().BeNull();
    }

    [Fact]
    public void LoadFromChanges_ClearsSelection_WhenFileMovesToDifferentLayer()
    {
        // The same path appearing under a different layer is a genuinely
        // different diff (e.g. an Unstaged modification became a Staged
        // modification once the user ran git add). Treat it as a fall-out
        // so the diff pane reloads with the new layer's content.
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs", layer: WorkingTreeLayer.Unstaged) },
            @"C:\repo", isCommitVsCommit: false);
        vm.SelectedEntry = vm.FlatEntries[0];

        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs", layer: WorkingTreeLayer.Staged) },
            @"C:\repo", isCommitVsCommit: false);

        vm.SelectedEntry.Should().BeNull();
    }

    [Fact]
    public void LoadFromChanges_LeavesSelectionNull_WhenNothingWasSelected()
    {
        var vm = new FileListViewModel();
        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs") },
            @"C:\repo", isCommitVsCommit: false);

        // No prior selection -> nothing to preserve, nothing to clear.
        vm.SelectedEntry.Should().BeNull();

        vm.LoadFromChanges(
            new[] { MakeChange("src/a.cs"), MakeChange("src/b.cs") },
            @"C:\repo", isCommitVsCommit: false);

        vm.SelectedEntry.Should().BeNull();
    }

    [Fact]
    public void DisplayMode_Switching_RecomputesEntryDisplayPaths()
    {
        var vm = new FileListViewModel();
        vm.LoadFromChanges(new[] { MakeChange("src/foo.cs") }, @"C:\repo", isCommitVsCommit: true);
        var entry = vm.FlatEntries[0];

        vm.DisplayMode = FileListDisplayMode.RepoRelative;
        entry.DisplayPath.Should().Be(@"src\foo.cs");

        vm.DisplayMode = FileListDisplayMode.FullPath;
        entry.DisplayPath.Should().Be(@"C:\repo\src\foo.cs");

        vm.DisplayMode = FileListDisplayMode.GroupedByDirectory;
        entry.DisplayPath.Should().Be("foo.cs");
    }

    [Fact]
    public void IsFullPathMode_Setter_UpdatesDisplayMode()
    {
        var vm = new FileListViewModel();
        vm.IsFullPathMode = true;
        vm.DisplayMode.Should().Be(FileListDisplayMode.FullPath);

        vm.IsRepoRelativeMode = true;
        vm.DisplayMode.Should().Be(FileListDisplayMode.RepoRelative);

        vm.IsGroupedByDirectoryMode = true;
        vm.DisplayMode.Should().Be(FileListDisplayMode.GroupedByDirectory);
    }
}
