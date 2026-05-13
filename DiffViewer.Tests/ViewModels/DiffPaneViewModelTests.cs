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
                DiffOptions = new DiffOptions(IgnoreWhitespace: true),
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
            var vm = new DiffPaneViewModel(repo, diff)
            {
                DiffOptions = new DiffOptions(IgnoreWhitespace: false),
            };
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
                DiffOptions = new DiffOptions(IgnoreWhitespace: true),
            };
            await vm.LoadAsync(Entry(ModifiedTextFile("a.cs")));
            bannerVisible = vm.IsWhitespaceOnlyBannerVisible;
        });

        bannerVisible.Should().BeFalse();
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
