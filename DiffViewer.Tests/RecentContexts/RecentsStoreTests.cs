using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.RecentContexts;

/// <summary>
/// Tests for the file-IO layer of recents.json. Each test uses a fresh
/// temp file so they can run in parallel without contention.
/// </summary>
public class RecentsStoreTests : IDisposable
{
    private readonly string _path;

    public RecentsStoreTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"recents-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsEmpty()
    {
        var doc = await RecentsStore.LoadAsync(_path);
        doc.Should().Be(RecentsDoc.Empty);
    }

    [Fact]
    public async Task LoadAsync_EmptyFile_ReturnsEmpty()
    {
        File.WriteAllText(_path, string.Empty);
        var doc = await RecentsStore.LoadAsync(_path);
        doc.Should().Be(RecentsDoc.Empty);
    }

    [Fact]
    public async Task LoadAsync_MalformedFile_ReturnsEmpty_DoesNotThrow()
    {
        File.WriteAllText(_path, "garbage{ not json");
        var doc = await RecentsStore.LoadAsync(_path);
        doc.Should().Be(RecentsDoc.Empty);
    }

    [Fact]
    public async Task ReadAndMutateAsync_FromEmpty_PersistsResult()
    {
        var added = MakeContext(@"C:\repos\foo", "main");

        var result = await RecentsStore.ReadAndMutateAsync(
            _path,
            current => RecentsDoc.From(new[] { added }));

        result.Items.Should().ContainSingle().Which.Should().Be(added);

        var reloaded = await RecentsStore.LoadAsync(_path);
        reloaded.Items.Should().ContainSingle().Which.Should().Be(added);
    }

    [Fact]
    public async Task ReadAndMutateAsync_TwoSequentialMutations_StackCorrectly()
    {
        var first = MakeContext(@"C:\repos\foo", "main");
        var second = MakeContext(@"C:\repos\bar", "develop");

        await RecentsStore.ReadAndMutateAsync(_path, _ => RecentsDoc.From(new[] { first }));
        await RecentsStore.ReadAndMutateAsync(_path, current => RecentsDoc.From(current.Items.Append(second)));

        var reloaded = await RecentsStore.LoadAsync(_path);
        reloaded.Items.Select(i => i.Identity.CanonicalRepoPath).Should().BeEquivalentTo(new[]
        {
            ContextIdentityFactory.CanonicalizeRepoPath(@"C:\repos\foo"),
            ContextIdentityFactory.CanonicalizeRepoPath(@"C:\repos\bar"),
        });
    }

    [Fact]
    public async Task ReadAndMutateAsync_ConcurrentAppenders_AllContributionsPersist()
    {
        // Spawn many writers each appending one unique entry. The
        // FileShare.None lock means they serialise; the test asserts
        // the read-modify-write loop preserves every contribution
        // (no lost-update).
        const int writers = 8;
        var tasks = new List<Task>(writers);
        for (var i = 0; i < writers; i++)
        {
            var entry = MakeContext($@"C:\repos\repo{i}", $"branch{i}");
            tasks.Add(Task.Run(async () =>
            {
                await RecentsStore.ReadAndMutateAsync(
                    _path,
                    current => RecentsDoc.From(current.Items.Append(entry)));
            }));
        }
        await Task.WhenAll(tasks);

        var reloaded = await RecentsStore.LoadAsync(_path);
        reloaded.Items.Should().HaveCount(writers);
        reloaded.Items
            .Select(i => i.Identity.CanonicalRepoPath)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ReadAndMutateAsync_WhenMutationThrows_FileLeftUnchanged()
    {
        // Seed with a known-good doc, then attempt a mutation that throws
        // BEFORE we touch the file body. The exception should propagate
        // and the previous content must remain intact.
        var seed = MakeContext(@"C:\repos\foo", "main");
        await RecentsStore.ReadAndMutateAsync(_path, _ => RecentsDoc.From(new[] { seed }));

        Func<Task> attempt = async () => await RecentsStore.ReadAndMutateAsync(
            _path,
            _ => throw new InvalidOperationException("boom"));

        await attempt.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        var reloaded = await RecentsStore.LoadAsync(_path);
        reloaded.Items.Should().ContainSingle().Which.Should().Be(seed);
    }

    [Fact]
    public async Task ReadAndMutateAsync_RetryWindowElapsed_ReleasesLockAndSucceeds()
    {
        // Hold the file open with FileShare.None on a background task
        // for shorter than the retry window; the foreground writer
        // should retry, succeed once the holder releases, and persist.
        var releaseHold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = Task.Run(async () =>
        {
            using var fs = new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            await releaseHold.Task;
        });

        // Give the holder a moment to acquire the lock.
        await Task.Delay(50);

        var entry = MakeContext(@"C:\repos\foo", "main");
        var writeTask = RecentsStore.ReadAndMutateAsync(
            _path,
            _ => RecentsDoc.From(new[] { entry }),
            retryWindow: TimeSpan.FromSeconds(2));

        // Release after a small delay so the writer has actually entered
        // its retry loop. Then the writer should succeed.
        _ = Task.Run(async () =>
        {
            await Task.Delay(150);
            releaseHold.SetResult(true);
        });

        await writeTask;
        await holder;

        var reloaded = await RecentsStore.LoadAsync(_path);
        reloaded.Items.Should().ContainSingle().Which.Should().Be(entry);
    }

    [Fact]
    public async Task ReadAndMutateAsync_NullPath_Throws()
    {
        Func<Task> act = async () => await RecentsStore.ReadAndMutateAsync(
            null!,
            d => d);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ReadAndMutateAsync_NullMutate_Throws()
    {
        Func<Task> act = async () => await RecentsStore.ReadAndMutateAsync(_path, null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private static RecentLaunchContext MakeContext(string repo, string leftRef)
    {
        var left = new DiffSide.CommitIsh(leftRef);
        var right = new DiffSide.WorkingTree();
        return new RecentLaunchContext(
            ContextIdentityFactory.Create(repo, left, right),
            left,
            right,
            DateTimeOffset.UtcNow);
    }
}
