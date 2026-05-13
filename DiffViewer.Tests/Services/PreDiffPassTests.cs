using System.Collections.Concurrent;
using System.Text;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

public sealed class PreDiffPassTests
{
    [Fact]
    public async Task Start_StampsEveryEligibleEntry()
    {
        var repo = new FakeRepo();
        var diff = new ScriptedDiffService(_ => true);
        using var pass = new PreDiffPass(repo, diff, maxConcurrency: 4, uiMarshaller: a => a());

        var entries = MakeEntries("a.txt", "b.txt", "c.txt");
        pass.Start(entries, selectedEntry: null, new DiffOptions());
        await WaitForStamps(entries);

        entries.Should().OnlyContain(e => e.HasVisibleDifferences == true);
    }

    [Fact]
    public async Task Start_SelectedEntryIsProcessedFirst()
    {
        var repo = new FakeRepo();
        var order = new ConcurrentQueue<string>();
        var diff = new ScriptedDiffService(left =>
        {
            order.Enqueue(left);
            return true;
        });
        // Single worker so order is observable and deterministic.
        using var pass = new PreDiffPass(repo, diff, maxConcurrency: 1, uiMarshaller: a => a());

        var entries = MakeEntries("a.txt", "b.txt", "c.txt", "d.txt");
        var selected = entries[2]; // c.txt

        pass.Start(entries, selected, new DiffOptions());
        await WaitForStamps(entries);

        order.First().Should().Be("c.txt-L");
    }

    [Fact]
    public async Task OnSelectionChanged_PromotesPendingEntryToHead()
    {
        var repo = new FakeRepo();
        var order = new ConcurrentQueue<string>();
        var gate = new SemaphoreSlim(0, 1);
        var diff = new ScriptedDiffService(left =>
        {
            order.Enqueue(left);
            // First call blocks until we release; subsequent calls run freely.
            if (order.Count == 1) gate.Wait();
            return true;
        });
        using var pass = new PreDiffPass(repo, diff, maxConcurrency: 1, uiMarshaller: a => a());

        var entries = MakeEntries("a.txt", "b.txt", "c.txt", "d.txt");
        pass.Start(entries, selectedEntry: null, new DiffOptions());

        // Wait for the worker to be parked inside diff #1.
        await WaitUntil(() => order.Count == 1, TimeSpan.FromSeconds(2));

        // Promote d.txt to the head of the queue.
        pass.OnSelectionChanged(entries[3]);
        gate.Release();

        await WaitForStamps(entries);

        var observed = order.ToArray();
        observed[0].Should().Be("a.txt-L");
        observed[1].Should().Be("d.txt-L"); // promoted ahead of b/c
    }

    [Fact]
    public async Task Start_RespectsMaxConcurrency()
    {
        var repo = new FakeRepo();
        var inFlight = 0;
        var peak = 0;
        var peakLock = new object();
        var diff = new ScriptedDiffService(_ =>
        {
            var current = Interlocked.Increment(ref inFlight);
            lock (peakLock) { if (current > peak) peak = current; }
            Thread.Sleep(40);
            Interlocked.Decrement(ref inFlight);
            return true;
        });
        using var pass = new PreDiffPass(repo, diff, maxConcurrency: 3, uiMarshaller: a => a());

        var entries = MakeEntries(Enumerable.Range(0, 20).Select(i => $"f{i:00}.txt").ToArray());
        pass.Start(entries, selectedEntry: null, new DiffOptions());
        await WaitForStamps(entries);

        peak.Should().BeLessThanOrEqualTo(3);
        peak.Should().BeGreaterThan(1, "concurrency=3 should actually parallelise on 20 entries");
    }

    [Fact]
    public async Task Start_SkipsBinaryAndLfsAndSparseAndModeOnlyAndSubmoduleAndConflicted()
    {
        var repo = new FakeRepo();
        var diff = new ScriptedDiffService(_ => true);
        using var pass = new PreDiffPass(repo, diff, uiMarshaller: a => a());

        var ok = MakeEntry("ok.txt");
        var binary = MakeEntry("binary.bin", isBinary: true);
        var lfs = MakeEntry("lfs.bin", isLfs: true);
        var sparse = MakeEntry("sparse.txt", isSparse: true);
        var modeOnly = MakeEntry("exec.sh", oldMode: 33188, newMode: 33261, sameSha: true);
        var submodule = MakeEntry("sub", status: FileStatus.SubmoduleMoved);
        var conflict = MakeEntry("conflict.txt", status: FileStatus.Conflicted);

        var all = new[] { ok, binary, lfs, sparse, modeOnly, submodule, conflict };
        pass.Start(all, selectedEntry: null, new DiffOptions());
        await WaitForStamps(new[] { ok });

        ok.HasVisibleDifferences.Should().Be(true);
        binary.HasVisibleDifferences.Should().BeNull();
        lfs.HasVisibleDifferences.Should().BeNull();
        sparse.HasVisibleDifferences.Should().BeNull();
        modeOnly.HasVisibleDifferences.Should().BeNull();
        submodule.HasVisibleDifferences.Should().BeNull();
        conflict.HasVisibleDifferences.Should().BeNull();
    }

    [Fact]
    public async Task Start_SkipsFilesAboveLargeFileThreshold()
    {
        var repo = new FakeRepo();
        var diff = new ScriptedDiffService(_ => true);
        using var pass = new PreDiffPass(repo, diff, largeFileThresholdBytes: 1024, uiMarshaller: a => a());

        var small = MakeEntry("small.txt", leftSize: 100, rightSize: 200);
        var bigLeft = MakeEntry("big-left.txt", leftSize: 5000, rightSize: 100);
        var bigRight = MakeEntry("big-right.txt", leftSize: 100, rightSize: 5000);

        var all = new[] { small, bigLeft, bigRight };
        pass.Start(all, selectedEntry: null, new DiffOptions());
        await WaitForStamps(new[] { small });

        small.HasVisibleDifferences.Should().Be(true);
        bigLeft.HasVisibleDifferences.Should().BeNull();
        bigRight.HasVisibleDifferences.Should().BeNull();
    }

    [Fact]
    public async Task Cancel_StopsInFlightWork()
    {
        var repo = new FakeRepo();
        var processed = 0;
        var blockUntilCancelled = new SemaphoreSlim(0, 1);
        var diff = new ScriptedDiffService(_ =>
        {
            Interlocked.Increment(ref processed);
            // First file sits indefinitely; cancel should release it.
            blockUntilCancelled.Wait(TimeSpan.FromSeconds(3));
            return true;
        });
        using var pass = new PreDiffPass(repo, diff, maxConcurrency: 1, uiMarshaller: a => a());

        var entries = MakeEntries("a.txt", "b.txt", "c.txt");
        pass.Start(entries, selectedEntry: null, new DiffOptions());

        // Wait for first diff to start.
        await WaitUntil(() => processed >= 1, TimeSpan.FromSeconds(2));

        pass.Cancel();
        blockUntilCancelled.Release();

        // Give the cancelled pass a moment to settle, then confirm later
        // entries never ran.
        await Task.Delay(120);

        entries[1].HasVisibleDifferences.Should().BeNull();
        entries[2].HasVisibleDifferences.Should().BeNull();
    }

    [Fact]
    public async Task Start_CancelsAnyInFlightPreviousPass()
    {
        var repo = new FakeRepo();
        var firstStarted = new TaskCompletionSource();
        var firstReleased = new SemaphoreSlim(0, 1);
        var seenSecond = false;

        var diff = new ScriptedDiffService(left =>
        {
            if (left.StartsWith("first-"))
            {
                firstStarted.TrySetResult();
                firstReleased.Wait();
            }
            else if (left.StartsWith("second-"))
            {
                seenSecond = true;
            }
            return true;
        });
        using var pass = new PreDiffPass(repo, diff, maxConcurrency: 1, uiMarshaller: a => a());

        var firstBatch = MakeEntries("first-a.txt", "first-b.txt");
        var secondBatch = MakeEntries("second-a.txt");

        pass.Start(firstBatch, selectedEntry: null, new DiffOptions());
        await firstStarted.Task;

        // Start a fresh pass while the previous one is parked.
        pass.Start(secondBatch, selectedEntry: null, new DiffOptions());
        firstReleased.Release();

        await WaitForStamps(secondBatch);

        seenSecond.Should().BeTrue();
        firstBatch[1].HasVisibleDifferences.Should().BeNull();
    }

    [Fact]
    public async Task OnOptionsChanged_ClearsStampsAndRestartsUnderNewOptions()
    {
        var repo = new FakeRepo();
        var seenOptions = new ConcurrentBag<bool>();
        var diff = new ScriptedDiffService((_, opts) =>
        {
            seenOptions.Add(opts.IgnoreWhitespace);
            return !opts.IgnoreWhitespace; // first pass: true; second pass: false
        });
        using var pass = new PreDiffPass(repo, diff, uiMarshaller: a => a());

        var entries = MakeEntries("a.txt", "b.txt");
        pass.Start(entries, selectedEntry: null, new DiffOptions(IgnoreWhitespace: false));
        await WaitForStamps(entries);
        entries.Should().OnlyContain(e => e.HasVisibleDifferences == true);

        pass.OnOptionsChanged(entries, selectedEntry: null, new DiffOptions(IgnoreWhitespace: true));
        await WaitForStamps(entries, expected: false);

        entries.Should().OnlyContain(e => e.HasVisibleDifferences == false);
        seenOptions.Should().Contain(true).And.Contain(false);
    }

    [Fact]
    public async Task Start_SkipsAlreadyStampedEntries()
    {
        var repo = new FakeRepo();
        var processed = new ConcurrentBag<string>();
        var diff = new ScriptedDiffService(left =>
        {
            processed.Add(left);
            return true;
        });
        using var pass = new PreDiffPass(repo, diff, uiMarshaller: a => a());

        var entries = MakeEntries("a.txt", "b.txt", "c.txt");
        entries[1].HasVisibleDifferences = true; // pre-stamped

        pass.Start(entries, selectedEntry: null, new DiffOptions());
        await WaitForStamps(new[] { entries[0], entries[2] });

        processed.Should().NotContain("b.txt-L");
        processed.Should().Contain("a.txt-L").And.Contain("c.txt-L");
    }

    [Fact]
    public async Task Dispose_CancelsAndIsIdempotent()
    {
        var repo = new FakeRepo();
        var blockUntilCancelled = new SemaphoreSlim(0, 1);
        var diff = new ScriptedDiffService(_ =>
        {
            blockUntilCancelled.Wait(TimeSpan.FromSeconds(3));
            return true;
        });
        var pass = new PreDiffPass(repo, diff, maxConcurrency: 1, uiMarshaller: a => a());

        var entries = MakeEntries("a.txt", "b.txt");
        pass.Start(entries, selectedEntry: null, new DiffOptions());
        await Task.Delay(40);

        pass.Dispose();
        pass.Dispose(); // double-dispose is a no-op
        blockUntilCancelled.Release();

        await Task.Delay(80);
        entries[1].HasVisibleDifferences.Should().BeNull();
    }

    // ---- helpers ----

    private static async Task WaitForStamps(IEnumerable<FileEntryViewModel> entries, bool? expected = null)
    {
        await WaitUntil(
            () => expected is null
                ? entries.All(e => e.HasVisibleDifferences is not null)
                : entries.All(e => e.HasVisibleDifferences == expected),
            TimeSpan.FromSeconds(5));
    }

    private static async Task WaitUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(15);
        }
        throw new TimeoutException("Predicate never became true.");
    }

    private static List<FileEntryViewModel> MakeEntries(params string[] names) =>
        names.Select(n => MakeEntry(n)).ToList();

    private static FileEntryViewModel MakeEntry(
        string path,
        bool isBinary = false,
        bool isLfs = false,
        bool isSparse = false,
        long leftSize = 100,
        long rightSize = 100,
        int oldMode = 33188,
        int newMode = 33188,
        bool sameSha = false,
        FileStatus status = FileStatus.Modified)
    {
        var sha = sameSha ? "sha-shared" : null;
        var change = new FileChange(
            Path: path,
            OldPath: null,
            Status: status,
            ConflictCode: status == FileStatus.Conflicted ? "UU" : null,
            Layer: WorkingTreeLayer.Unstaged,
            LeftBlobSha: sha,
            RightBlobSha: sha,
            LeftFileSizeBytes: leftSize,
            RightFileSizeBytes: rightSize,
            IsBinary: isBinary,
            IsLfsPointer: isLfs,
            IsSparseNotCheckedOut: isSparse,
            OldMode: oldMode,
            NewMode: newMode);
        return new FileEntryViewModel(change, @"C:\repo");
    }

    private sealed class FakeRepo : IRepositoryService
    {
        public RepositoryShape Shape => new(@"C:\repo", @"C:\repo", @"C:\repo\.git", false, false, false, false, false);
        public IReadOnlyList<FileChange> CurrentChanges { get; } = Array.Empty<FileChange>();
        public event EventHandler<ChangeListUpdatedEventArgs>? ChangeListUpdated { add { } remove { } }
        public event EventHandler<RepositoryLostEventArgs>? RepositoryLost { add { } remove { } }

        public string? ResolveCommitIsh(string reference) => reference;
        public bool ValidateRevisions(string leftRef, string rightRef) => true;
        public IReadOnlyList<FileChange> EnumerateChanges(DiffSide left, DiffSide right) => Array.Empty<FileChange>();
        public BlobContent ReadSide(FileChange change, ChangeSide side)
        {
            // Synthetic content: "{path}-L" / "{path}-R" so tests can observe order.
            var text = side == ChangeSide.Left ? $"{change.Path}-L" : $"{change.Path}-R";
            return new BlobContent(Encoding.UTF8.GetBytes(text), Encoding.UTF8, text, false, false);
        }
        public void RefreshIndex() { }
        public FileChange? TryResolveCurrent(string path, WorkingTreeLayer layer) => null;
        public bool TryReopen() => true;
        public bool IsPathIgnored(string repoRelativeForwardSlashPath) => false;
        public (IReadOnlyList<FileChange> Snapshot, IDisposable Subscription) SnapshotAndSubscribe(
            EventHandler<ChangeListUpdatedEventArgs> handler) =>
            (CurrentChanges, new DummyDisposable());
        public void Dispose() { }
        private sealed class DummyDisposable : IDisposable { public void Dispose() { } }
    }

    /// <summary>
    /// Test diff service whose <see cref="HasVisibleDifferences"/> result is
    /// driven by a script. Other members throw so accidental calls fail loudly.
    /// </summary>
    private sealed class ScriptedDiffService : IDiffService
    {
        private readonly Func<string, DiffOptions, bool> _script;
        public ScriptedDiffService(Func<string, bool> script) : this((l, _) => script(l)) { }
        public ScriptedDiffService(Func<string, DiffOptions, bool> script) { _script = script; }

        public bool HasVisibleDifferences(string left, string right, DiffOptions options) => _script(left, options);
        public DiffComputation ComputeDiff(string left, string right, DiffOptions options) => throw new NotSupportedException();
        public string FormatUnified(string oldPath, string newPath, IReadOnlyList<DiffHunk> hunks, string leftSource, string rightSource) => throw new NotSupportedException();
        public IReadOnlyList<IntraLinePiece> ComputeIntraLineDiff(string oldLine, string newLine, bool ignoreWhitespace) => throw new NotSupportedException();
    }
}
