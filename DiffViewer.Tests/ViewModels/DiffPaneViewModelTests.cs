using System.Text;
using System.Windows.Threading;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

public class DiffPaneViewModelTests
{
    private static FileChange ModifiedTextFile(string path) =>
        new(
            Path: path,
            OldPath: null,
            Status: Models.FileStatus.Modified,
            ConflictCode: null,
            Layer: WorkingTreeLayer.Unstaged,
            LeftBlobSha: "aaaaaaa", RightBlobSha: "bbbbbbb",
            IsBinary: false,
            LeftFileSizeBytes: null, RightFileSizeBytes: null,
            IsLfsPointer: false, IsSparseNotCheckedOut: false,
            OldMode: 0, NewMode: 0);

    private static FileChange Binary(string path) =>
        ModifiedTextFile(path) with { IsBinary = true };

    private static FileChange Lfs(string path) =>
        ModifiedTextFile(path) with { IsLfsPointer = true };

    private static FileChange Submodule(string path) =>
        new(path, null, Models.FileStatus.SubmoduleMoved, null, WorkingTreeLayer.Unstaged,
            "1111111", "2222222", false, null, null, false, false, 0, 0);

    private static FileChange ModeOnly(string path) =>
        new(path, null, Models.FileStatus.TypeChanged, null, WorkingTreeLayer.Unstaged,
            "abcdefg", "abcdefg", false, null, null, false, false, 0x1A4, 0x1ED); // 0644, 0755

    private static FileEntryViewModel Entry(FileChange change) =>
        new(change, @"C:\repo");

    [Fact]
    public async Task LoadAsync_WithNullEntry_ShowsSelectAFilePlaceholder()
    {
        var repo = new FakeRepository();
        var vm = new DiffPaneViewModel(repo);

        await vm.LoadAsync(null);

        vm.PlaceholderMessage.Should().NotBeNull();
        vm.ShowPlaceholder.Should().BeTrue();
        vm.ShowEditors.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_BinaryFile_ShowsBinaryPlaceholder()
    {
        var repo = new FakeRepository();
        var vm = new DiffPaneViewModel(repo);

        await vm.LoadAsync(Entry(Binary("img.png")));

        vm.ShowPlaceholder.Should().BeTrue();
        vm.PlaceholderMessage.Should().Contain("Binary");
        repo.ReadCount.Should().Be(0);
    }

    [Fact]
    public async Task LoadAsync_LfsPointer_ShowsLfsPlaceholder()
    {
        var repo = new FakeRepository();
        var vm = new DiffPaneViewModel(repo);

        await vm.LoadAsync(Entry(Lfs("big.bin")));

        vm.PlaceholderMessage.Should().Contain("LFS");
        repo.ReadCount.Should().Be(0);
    }

    [Fact]
    public async Task LoadAsync_Submodule_ShowsSubmodulePlaceholder()
    {
        var repo = new FakeRepository();
        var vm = new DiffPaneViewModel(repo);

        await vm.LoadAsync(Entry(Submodule("vendor/lib")));

        vm.PlaceholderMessage.Should().Contain("Submodule");
    }

    [Fact]
    public async Task LoadAsync_ModeOnly_ShowsModePlaceholder()
    {
        var repo = new FakeRepository();
        var vm = new DiffPaneViewModel(repo);

        await vm.LoadAsync(Entry(ModeOnly("script.sh")));

        vm.PlaceholderMessage.Should().Contain("Mode");
    }

    [Fact]
    public async Task LoadAsync_TextFile_PopulatesBothDocuments()
    {
        var repo = new FakeRepository
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha\ngamma\n",
        };

        string? leftText = null;
        string? rightText = null;
        bool? showEditors = null;
        bool? showPlaceholder = null;

        await RunOnUiSyncContextAsync(async () =>
        {
            // The TextDocument is a DispatcherObject - construct it on the
            // dispatcher thread so the LoadAsync continuation can write to it,
            // and read its Text inside the same thread.
            var vm = new DiffPaneViewModel(repo);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            leftText = vm.LeftDocument.Text;
            rightText = vm.RightDocument.Text;
            showEditors = vm.ShowEditors;
            showPlaceholder = vm.ShowPlaceholder;
        });

        leftText.Should().Be("alpha\nbeta\n");
        rightText.Should().Be("alpha\ngamma\n");
        showEditors.Should().BeTrue();
        showPlaceholder.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_TextFile_WithDiffService_PopulatesHighlightMap()
    {
        var repo = new FakeRepository
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha\nBETA\n",
        };
        var diff = new DiffService();

        int leftLineCount = 0;
        int rightLineCount = 0;
        int eventFireCount = 0;

        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            vm.HighlightMapChanged += (_, _) => eventFireCount++;
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            leftLineCount = vm.HighlightMap.LeftLines.Count;
            rightLineCount = vm.HighlightMap.RightLines.Count;
        });

        leftLineCount.Should().BeGreaterThan(0);
        rightLineCount.Should().BeGreaterThan(0);
        eventFireCount.Should().Be(1);
    }

    [Fact]
    public async Task LoadAsync_WhitespaceOnlyDiff_WithIgnoreWhitespace_ShowsBanner()
    {
        var repo = new FakeRepository
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha   \nbeta\n",
        };
        var diff = new DiffService();

        bool? bannerVisible = null;
        int? hunkCount = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff)
            {
                IgnoreWhitespace = true,
            };
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            bannerVisible = vm.IsWhitespaceOnlyBannerVisible;
            hunkCount = vm.HighlightMap.LeftLines.Count + vm.HighlightMap.RightLines.Count;
        });

        bannerVisible.Should().BeTrue();
        hunkCount.Should().Be(0);
    }

    [Fact]
    public async Task LoadAsync_WhitespaceOnlyDiff_WithoutIgnoreWhitespace_HidesBanner()
    {
        var repo = new FakeRepository
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha   \nbeta\n",
        };
        var diff = new DiffService();

        bool? bannerVisible = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            bannerVisible = vm.IsWhitespaceOnlyBannerVisible;
        });

        bannerVisible.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_NoActualDifference_DoesNotShowBanner()
    {
        var repo = new FakeRepository
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha\nbeta\n",
        };
        var diff = new DiffService();

        bool? bannerVisible = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff)
            {
                IgnoreWhitespace = true,
            };
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            bannerVisible = vm.IsWhitespaceOnlyBannerVisible;
        });

        bannerVisible.Should().BeFalse();
    }

    [Fact]
    public async Task IgnoreWhitespaceToggle_AfterLoad_RecomputesDiffAndUpdatesBanner()
    {
        var repo = new FakeRepository
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha   \nbeta\n",
        };
        var diff = new DiffService();

        bool? bannerAfterToggleOn = null;
        bool? bannerAfterToggleOff = null;
        int eventCount = 0;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            vm.HighlightMapChanged += (_, _) => eventCount++;

            vm.IgnoreWhitespace = true;
            bannerAfterToggleOn = vm.IsWhitespaceOnlyBannerVisible;

            vm.IgnoreWhitespace = false;
            bannerAfterToggleOff = vm.IsWhitespaceOnlyBannerVisible;
        });

        bannerAfterToggleOn.Should().BeTrue();
        bannerAfterToggleOff.Should().BeFalse();
        eventCount.Should().Be(2);
    }

    [Fact]
    public async Task ShowIntraLineDiff_DefaultsToTrue()
    {
        var repo = new FakeRepository();
        var diff = new DiffService();

        bool? intra = null;
        await RunOnUiSyncContextAsync(() =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            intra = vm.ShowIntraLineDiff;
            return Task.CompletedTask;
        });

        intra.Should().BeTrue();
    }

    [Fact]
    public async Task IsSideBySide_FalseFlipsShowInline()
    {
        var repo = new FakeRepository { LeftText = "a\n", RightText = "b\n" };
        var diff = new DiffService();

        bool? sideBefore = null, inlineBefore = null, sideAfter = null, inlineAfter = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            sideBefore = vm.ShowSideBySide;
            inlineBefore = vm.ShowInline;

            vm.IsSideBySide = false;
            sideAfter = vm.ShowSideBySide;
            inlineAfter = vm.ShowInline;
        });

        sideBefore.Should().BeTrue();
        inlineBefore.Should().BeFalse();
        sideAfter.Should().BeFalse();
        inlineAfter.Should().BeTrue();
    }

    [Fact]
    public async Task IsLiveUpdatesAvailable_ReflectsCommitVsCommitFlag()
    {
        var repo = new FakeRepository();

        bool? wtAvailable = null;
        bool? cvcAvailable = null;
        await RunOnUiSyncContextAsync(() =>
        {
            var workingTreeVm = new DiffPaneViewModel(repo, isCommitVsCommit: false);
            var commitVsCommitVm = new DiffPaneViewModel(repo, isCommitVsCommit: true);
            wtAvailable = workingTreeVm.IsLiveUpdatesAvailable;
            cvcAvailable = commitVsCommitVm.IsLiveUpdatesAvailable;
            return Task.CompletedTask;
        });

        wtAvailable.Should().BeTrue();
        cvcAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task NavigateNextHunk_CyclesThroughHunksAndRaisesEvent()
    {
        var repo = new FakeRepository
        {
            LeftText = "one\ntwo\nthree\nfour\nfive\nsix\nseven\neight\n",
            RightText = "ONE\ntwo\nthree\nfour\nfive\nsix\nseven\nEIGHT\n",
        };
        var diff = new DiffService();

        var visited = new List<int>();
        var leftLines = new List<int>();
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            vm.HunkNavigationRequested += (_, args) =>
            {
                visited.Add(args.HunkIndex);
                leftLines.Add(args.LeftLine);
            };
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            int hunkCount = vm.CurrentHunks.Count;
            // Walk forward (hunkCount + 1) steps to prove we cycle back to 0.
            for (int i = 0; i < hunkCount + 1; i++)
            {
                vm.NavigateNextHunkCommand.Execute(null);
            }
        });

        visited.Should().NotBeEmpty();
        // Last visited should equal index 0 (cycle).
        visited[^1].Should().Be(0);
    }

    [Fact]
    public async Task NavigatePreviousHunk_OnFreshLoad_GoesToLastHunk()
    {
        var repo = new FakeRepository
        {
            LeftText = "one\ntwo\nthree\nfour\nfive\nsix\nseven\neight\n",
            RightText = "ONE\ntwo\nthree\nfour\nfive\nsix\nseven\nEIGHT\n",
        };
        var diff = new DiffService();

        int? lastVisited = null;
        int? hunkCount = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            vm.HunkNavigationRequested += (_, args) => lastVisited = args.HunkIndex;
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            hunkCount = vm.CurrentHunks.Count;
            vm.NavigatePreviousHunkCommand.Execute(null);
        });

        hunkCount.Should().BeGreaterThan(0);
        lastVisited.Should().Be(hunkCount!.Value - 1);
    }

    [Fact]
    public async Task NavigateNextHunk_WithNoHunks_DoesNotRaiseEvent()
    {
        var repo = new FakeRepository();
        var diff = new DiffService();

        bool eventFired = false;
        await RunOnUiSyncContextAsync(() =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            vm.HunkNavigationRequested += (_, _) => eventFired = true;
            vm.NavigateNextHunkCommand.Execute(null);
            return Task.CompletedTask;
        });

        eventFired.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_TextFile_PopulatesInlineDocument()
    {
        var repo = new FakeRepository
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha\ngamma\n",
        };
        var diff = new DiffService();

        string? inlineText = null;
        int? lineKindCount = null;
        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            inlineText = vm.InlineDocument.Text;
            lineKindCount = vm.InlineLineKinds.Count;
        });

        inlineText.Should().NotBeNullOrEmpty();
        inlineText.Should().Contain("@@");
        lineKindCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// DiffPaneViewModel.LoadAsync uses TaskScheduler.FromCurrentSynchronizationContext()
    /// for its continuation; awaiting it from a plain xunit thread without a
    /// SynchronizationContext deadlocks. Wrap in a minimal Dispatcher pump.
    /// </summary>
    private static async Task RunOnUiSyncContextAsync(Func<Task> body)
    {
        var tcs = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try { await body(); tcs.SetResult(); }
                catch (Exception ex) { tcs.SetException(ex); }
                finally { dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await tcs.Task;
    }

    private sealed class FakeRepository : IRepositoryService
    {
        public string LeftText { get; set; } = string.Empty;
        public string RightText { get; set; } = string.Empty;
        public int ReadCount;

        public RepositoryShape Shape => new(@"C:\repo", @"C:\repo", @"C:\repo\.git", false, false, false, false, false);
        public IReadOnlyList<FileChange> CurrentChanges { get; } = Array.Empty<FileChange>();

        public event EventHandler<ChangeListUpdatedEventArgs>? ChangeListUpdated { add { } remove { } }
        public event EventHandler<RepositoryLostEventArgs>? RepositoryLost { add { } remove { } }

        public string? ResolveCommitIsh(string reference) => reference;
        public bool ValidateRevisions(string leftRef, string rightRef) => true;
        public IReadOnlyList<FileChange> EnumerateChanges(DiffSide left, DiffSide right) => Array.Empty<FileChange>();

        public BlobContent ReadSide(FileChange change, ChangeSide side)
        {
            ReadCount++;
            var text = side == ChangeSide.Left ? LeftText : RightText;
            return new BlobContent(Encoding.UTF8.GetBytes(text), Encoding.UTF8, text, false, false);
        }

        public void RefreshIndex() { }
        public FileChange? TryResolveCurrent(string path, WorkingTreeLayer layer) => null;
        public bool TryReopen() => true;
        public (IReadOnlyList<FileChange> Snapshot, IDisposable Subscription) SnapshotAndSubscribe(
            EventHandler<ChangeListUpdatedEventArgs> handler) =>
            (CurrentChanges, new DummyDisposable());
        public void Dispose() { }

        private sealed class DummyDisposable : IDisposable { public void Dispose() { } }
    }
}
