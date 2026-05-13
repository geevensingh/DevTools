using DiffViewer.Models;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

public class DirectoryNodeViewModelTests
{
    private static FileEntryViewModel Entry(string repoRelPath)
    {
        var change = new FileChange(
            Path: repoRelPath,
            OldPath: null,
            Status: Models.FileStatus.Modified,
            ConflictCode: null,
            Layer: WorkingTreeLayer.Unstaged,
            LeftBlobSha: null, RightBlobSha: null,
            IsBinary: false,
            LeftFileSizeBytes: null, RightFileSizeBytes: null,
            IsLfsPointer: false, IsSparseNotCheckedOut: false,
            OldMode: 0, NewMode: 0);
        return new FileEntryViewModel(change, @"C:\repo");
    }

    [Fact]
    public void Build_GroupsFilesUnderTheirDirectories()
    {
        var roots = DirectoryNodeViewModel.Build(new[]
        {
            Entry("src/a.cs"),
            Entry("src/b.cs"),
            Entry("docs/readme.md"),
        }).ToList();

        roots.Select(r => r.Label).Should().Equal("docs", "src");
        roots[1].Files.Select(f => f.FileName).Should().Equal("a.cs", "b.cs");
    }

    [Fact]
    public void Build_CollapsesSingleChildChains()
    {
        var roots = DirectoryNodeViewModel.Build(new[]
        {
            Entry("a/b/c/leaf.cs"),
        }).ToList();

        roots.Should().ContainSingle();
        roots[0].Label.Should().Be(@"a\b\c");
        roots[0].Files.Should().ContainSingle();
        roots[0].Files[0].FileName.Should().Be("leaf.cs");
    }

    [Fact]
    public void Build_DoesNotCollapseAcrossDirectoriesWithFiles()
    {
        var roots = DirectoryNodeViewModel.Build(new[]
        {
            Entry("a/x.cs"),
            Entry("a/b/c/leaf.cs"),
        }).ToList();

        roots.Should().ContainSingle();
        roots[0].Label.Should().Be("a");
        roots[0].Files.Select(f => f.FileName).Should().Contain("x.cs");
        roots[0].Children.Should().ContainSingle();
        roots[0].Children[0].Label.Should().Be(@"b\c");
    }

    [Fact]
    public void Build_PutsRootFilesUnderSyntheticRootNode()
    {
        var roots = DirectoryNodeViewModel.Build(new[]
        {
            Entry("README.md"),
            Entry("src/a.cs"),
        }).ToList();

        roots.Should().HaveCount(2);
        roots[0].Label.Should().BeEmpty();
        roots[0].Files.Select(f => f.FileName).Should().Equal("README.md");
        roots[1].Label.Should().Be("src");
    }

    [Fact]
    public void ChildrenAndFiles_YieldsChildrenFirstThenFiles()
    {
        var roots = DirectoryNodeViewModel.Build(new[]
        {
            Entry("a/x.cs"),
            Entry("a/b/c/leaf.cs"),
        }).ToList();

        var combined = roots[0].ChildrenAndFiles.ToList();
        combined.Should().HaveCount(2);
        combined[0].Should().BeOfType<DirectoryNodeViewModel>();
        combined[1].Should().BeOfType<FileEntryViewModel>();
    }

    [Fact]
    public void Build_DefaultsAllNodesToExpanded()
    {
        var roots = DirectoryNodeViewModel.Build(new[]
        {
            Entry("a/b/x.cs"),
            Entry("a/b/y.cs"),
        }).ToList();

        roots[0].IsExpanded.Should().BeTrue();
    }

    [Fact]
    public void Build_WithStore_CollapsedStateSurvivesRebuild()
    {
        var store = new DirectoryExpansionStore();

        var first = DirectoryNodeViewModel.Build(
            new[] { Entry("src/a.cs"), Entry("docs/readme.md") },
            sectionKey: "Unstaged",
            store: store).ToList();

        // User collapses "src".
        var src = first.Single(r => r.Label == "src");
        src.IsExpanded = false;

        // Simulate a watcher-fired reload: a new file appears, sections rebuild.
        var second = DirectoryNodeViewModel.Build(
            new[] { Entry("src/a.cs"), Entry("docs/readme.md"), Entry("src/c.cs") },
            sectionKey: "Unstaged",
            store: store).ToList();

        second.Single(r => r.Label == "src").IsExpanded.Should().BeFalse();
        second.Single(r => r.Label == "docs").IsExpanded.Should().BeTrue();
    }

    [Fact]
    public void Build_WithStore_CollapsingNestedNodeSurvivesRebuild()
    {
        var store = new DirectoryExpansionStore();

        var first = DirectoryNodeViewModel.Build(
            new[] { Entry("a/x.cs"), Entry("a/b/c/leaf.cs") },
            sectionKey: "Unstaged",
            store: store).ToList();

        // User collapses the chained "b\c" child of "a".
        first[0].Children.Single().IsExpanded = false;

        var second = DirectoryNodeViewModel.Build(
            new[] { Entry("a/x.cs"), Entry("a/b/c/leaf.cs"), Entry("a/b/c/another.cs") },
            sectionKey: "Unstaged",
            store: store).ToList();

        second[0].Children.Single().IsExpanded.Should().BeFalse();
    }

    [Fact]
    public void Build_WithStore_DifferentSectionsHaveIndependentExpansionState()
    {
        var store = new DirectoryExpansionStore();

        var staged = DirectoryNodeViewModel.Build(
            new[] { Entry("src/a.cs") },
            sectionKey: "Staged",
            store: store).ToList();
        staged.Single().IsExpanded = false;

        var unstaged = DirectoryNodeViewModel.Build(
            new[] { Entry("src/b.cs") },
            sectionKey: "Unstaged",
            store: store).ToList();

        unstaged.Single().IsExpanded.Should().BeTrue();
    }
}
