using System.IO;
using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

/// <summary>
/// Integration tests for <see cref="RepositoryWatcher"/> that use real
/// <see cref="FileSystemWatcher"/> instances over a temp directory pair.
/// Kept small + serial so they don't introduce flake from FSW timing.
/// Pure filter / debounce logic is covered exhaustively by
/// <see cref="RepositoryEventDebouncerTests"/>.
/// </summary>
[Collection(nameof(RepositoryWatcherCollection))]
public class RepositoryWatcherTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _workingDir;
    private readonly string _gitDir;
    private static readonly TimeSpan FastDebounce = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan WaitForFire = TimeSpan.FromMilliseconds(800);

    public RepositoryWatcherTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DiffViewer.WatcherTests", Guid.NewGuid().ToString("N"));
        _workingDir = Path.Combine(_tempRoot, "wt");
        _gitDir = Path.Combine(_tempRoot, "wt", ".git");
        Directory.CreateDirectory(_workingDir);
        Directory.CreateDirectory(_gitDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public void IsTrackedGitDirFile_AllowsKnownNames_AndRejectsNoise()
    {
        RepositoryWatcher.IsTrackedGitDirFile("HEAD").Should().BeTrue();
        RepositoryWatcher.IsTrackedGitDirFile("index").Should().BeTrue();
        RepositoryWatcher.IsTrackedGitDirFile("MERGE_HEAD").Should().BeTrue();
        RepositoryWatcher.IsTrackedGitDirFile("REBASE_HEAD").Should().BeTrue();
        RepositoryWatcher.IsTrackedGitDirFile("CHERRY_PICK_HEAD").Should().BeTrue();
        RepositoryWatcher.IsTrackedGitDirFile("REVERT_HEAD").Should().BeTrue();
        RepositoryWatcher.IsTrackedGitDirFile("BISECT_LOG").Should().BeTrue();
        RepositoryWatcher.IsTrackedGitDirFile("HEAD.lock").Should().BeTrue();
        RepositoryWatcher.IsTrackedGitDirFile("index.lock").Should().BeTrue();

        RepositoryWatcher.IsTrackedGitDirFile("FETCH_HEAD").Should().BeFalse();
        RepositoryWatcher.IsTrackedGitDirFile("ORIG_HEAD").Should().BeFalse();
        RepositoryWatcher.IsTrackedGitDirFile("packed-refs").Should().BeFalse();
        RepositoryWatcher.IsTrackedGitDirFile("config").Should().BeFalse();
        RepositoryWatcher.IsTrackedGitDirFile("hEaD").Should().BeFalse(); // case sensitive on disk
        RepositoryWatcher.IsTrackedGitDirFile("").Should().BeFalse();
    }

    [Fact]
    public void ToRepoRelativeForwardSlash_NormalisesPathsCorrectly()
    {
        using var watcher = new RepositoryWatcher(_workingDir, _gitDir, _ => false, FastDebounce);

        string nested = Path.Combine(_workingDir, "src", "Foo.cs");
        watcher.ToRepoRelativeForwardSlash(nested).Should().Be("src/Foo.cs");

        watcher.ToRepoRelativeForwardSlash(Path.Combine(_tempRoot, "outside.txt")).Should().BeEmpty();
    }

    [Fact]
    public void Start_FileCreatedInWorkingTree_FiresChanged()
    {
        using var watcher = new RepositoryWatcher(_workingDir, _gitDir, _ => false, FastDebounce);
        var fired = new ManualResetEventSlim();
        RepositoryChangeKind capturedKind = RepositoryChangeKind.None;
        watcher.Changed += (_, e) => { capturedKind = e.Kind; fired.Set(); };

        watcher.Start();
        Thread.Sleep(50); // let FSW settle
        File.WriteAllText(Path.Combine(_workingDir, "hello.txt"), "world");

        fired.Wait(WaitForFire).Should().BeTrue();
        capturedKind.Should().HaveFlag(RepositoryChangeKind.WorkingTree);
    }

    [Fact]
    public void Start_IgnoredPath_DoesNotFire()
    {
        // Mark every path under "ignored/" as ignored.
        Func<string, bool> ignore = rel => rel.StartsWith("ignored/", StringComparison.Ordinal);
        Directory.CreateDirectory(Path.Combine(_workingDir, "ignored"));

        using var watcher = new RepositoryWatcher(_workingDir, _gitDir, ignore, FastDebounce);
        int fireCount = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref fireCount);

        watcher.Start();
        Thread.Sleep(50);
        File.WriteAllText(Path.Combine(_workingDir, "ignored", "noise.txt"), "noise");

        Thread.Sleep(WaitForFire);
        fireCount.Should().Be(0);
    }

    [Fact]
    public void Start_GitHeadChange_FiresChangedWithGitDirKind()
    {
        using var watcher = new RepositoryWatcher(_workingDir, _gitDir, _ => false, FastDebounce);
        var fired = new ManualResetEventSlim();
        RepositoryChangeKind capturedKind = RepositoryChangeKind.None;
        watcher.Changed += (_, e) => { capturedKind = e.Kind; fired.Set(); };

        watcher.Start();
        Thread.Sleep(50);
        File.WriteAllText(Path.Combine(_gitDir, "HEAD"), "ref: refs/heads/main\n");

        fired.Wait(WaitForFire).Should().BeTrue();
        capturedKind.Should().HaveFlag(RepositoryChangeKind.GitDir);
    }

    [Fact]
    public void Start_GitNoiseFile_DoesNotFire()
    {
        using var watcher = new RepositoryWatcher(_workingDir, _gitDir, _ => false, FastDebounce);
        int fireCount = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref fireCount);

        watcher.Start();
        Thread.Sleep(50);
        // FETCH_HEAD is explicitly NOT in the IsTrackedGitDirFile allow-list.
        File.WriteAllText(Path.Combine(_gitDir, "FETCH_HEAD"), "noise");

        Thread.Sleep(WaitForFire);
        fireCount.Should().Be(0);
    }

    [Fact]
    public void Suspend_BlocksFireUntilDisposed()
    {
        using var watcher = new RepositoryWatcher(_workingDir, _gitDir, _ => false, FastDebounce);
        int fireCount = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref fireCount);

        watcher.Start();
        Thread.Sleep(50);

        var token = watcher.Suspend();
        File.WriteAllText(Path.Combine(_workingDir, "hello.txt"), "world");
        Thread.Sleep(WaitForFire);
        fireCount.Should().Be(0);

        token.Dispose();
        // The pending event fires synchronously on resume.
        fireCount.Should().Be(1);
    }
}

[CollectionDefinition(nameof(RepositoryWatcherCollection), DisableParallelization = true)]
public sealed class RepositoryWatcherCollection { }
