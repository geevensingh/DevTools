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

public class RecentContextsServiceTests : IDisposable
{
    private readonly string _path;

    public RecentContextsServiceTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"recents-svc-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { /* best-effort */ }
    }

    [Fact]
    public void Current_BeforeLoad_IsEmpty()
    {
        var svc = new RecentContextsService(_path);
        svc.Current.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_FromMissingFile_LeavesEmptyAndRaisesChanged()
    {
        var svc = new RecentContextsService(_path);
        var fired = 0;
        svc.Changed += (_, _) => fired++;

        await svc.LoadAsync();

        svc.Current.Should().BeEmpty();
        fired.Should().Be(1);
    }

    [Fact]
    public async Task LoadAsync_FromPopulatedFile_HydratesMruSorted()
    {
        var older = MakeContext(@"C:\repos\old", "main", DateTimeOffset.UtcNow.AddDays(-5));
        var newer = MakeContext(@"C:\repos\new", "main", DateTimeOffset.UtcNow.AddMinutes(-1));
        await RecentsStore.ReadAndMutateAsync(_path, _ => RecentsDoc.From(new[] { older, newer }));

        var svc = new RecentContextsService(_path);
        await svc.LoadAsync();

        svc.Current.Select(i => i.Identity.CanonicalRepoPath).Should()
            .ContainInOrder(
                ContextIdentityFactory.CanonicalizeRepoPath(@"C:\repos\new"),
                ContextIdentityFactory.CanonicalizeRepoPath(@"C:\repos\old"));
    }

    [Fact]
    public async Task RecordLaunchAsync_AppendsAndPersists()
    {
        var svc = new RecentContextsService(_path);
        await svc.LoadAsync();

        var (id, left, right) = MakeIdentity(@"C:\repos\foo", "main");
        await svc.RecordLaunchAsync(id, left, right);

        svc.Current.Should().ContainSingle();
        svc.Current[0].Identity.Should().Be(id);

        var reloaded = new RecentContextsService(_path);
        await reloaded.LoadAsync();
        reloaded.Current.Should().ContainSingle()
            .Which.Identity.Should().Be(id);
    }

    [Fact]
    public async Task RecordLaunchAsync_RaisesChangedEvent()
    {
        var svc = new RecentContextsService(_path);
        await svc.LoadAsync();
        var fired = 0;
        svc.Changed += (_, _) => fired++;

        var (id, left, right) = MakeIdentity(@"C:\repos\foo", "main");
        await svc.RecordLaunchAsync(id, left, right);

        fired.Should().Be(1);
    }

    [Fact]
    public async Task RecordLaunchAsync_DedupsBySameIdentity_KeepingNewerTimestamp()
    {
        var svc = new RecentContextsService(_path);
        var (id, left, right) = MakeIdentity(@"C:\repos\foo", "main");

        await svc.RecordLaunchAsync(id, left, right);
        var firstStamp = svc.Current[0].LastUsedUtc;
        await Task.Delay(20);
        await svc.RecordLaunchAsync(id, left, right);

        svc.Current.Should().ContainSingle();
        svc.Current[0].LastUsedUtc.Should().BeAfter(firstStamp);
    }

    [Fact]
    public async Task RecordLaunchAsync_DedupsCaseInsensitivelyOnPath()
    {
        var svc = new RecentContextsService(_path);
        var (id1, left, right) = MakeIdentity(@"C:\Repos\Foo", "main");
        var (id2, _, _) = MakeIdentity(@"c:\repos\foo", "main");

        await svc.RecordLaunchAsync(id1, left, right);
        await svc.RecordLaunchAsync(id2, left, right);

        svc.Current.Should().ContainSingle("paths differ only in case so they refer to the same dir");
    }

    [Fact]
    public async Task RecordLaunchAsync_DistinguishesByDiffSide()
    {
        var svc = new RecentContextsService(_path);
        var (id1, l1, r1) = MakeIdentity(@"C:\repos\foo", "main");
        var (id2, l2, r2) = MakeIdentity(@"C:\repos\foo", "develop");

        await svc.RecordLaunchAsync(id1, l1, r1);
        await svc.RecordLaunchAsync(id2, l2, r2);

        svc.Current.Should().HaveCount(2);
    }

    [Fact]
    public async Task RecordLaunchAsync_DistinguishesByCommitIshCase()
    {
        var svc = new RecentContextsService(_path);
        var (id1, l1, r1) = MakeIdentity(@"C:\repos\foo", "HEAD");
        var (id2, l2, r2) = MakeIdentity(@"C:\repos\foo", "head");

        await svc.RecordLaunchAsync(id1, l1, r1);
        await svc.RecordLaunchAsync(id2, l2, r2);

        svc.Current.Should().HaveCount(2, "CommitIsh refs are case-sensitive by design");
    }

    [Fact]
    public async Task RecordLaunchAsync_CapsAtTen_DroppingOldest()
    {
        var svc = new RecentContextsService(_path);

        // Insert 12 distinct entries spread across time, oldest first.
        for (var i = 0; i < 12; i++)
        {
            var (id, left, right) = MakeIdentity($@"C:\repos\repo{i}", "main");
            await svc.RecordLaunchAsync(id, left, right);
            await Task.Delay(2); // ensure distinct timestamps
        }

        svc.Current.Should().HaveCount(RecentContextsService.MaxEntries);
        // Newest is the most-recently-recorded (repo11).
        svc.Current[0].Identity.CanonicalRepoPath.Should().EndWith("repo11");
        // Oldest two (repo0, repo1) should be evicted.
        svc.Current.Select(i => i.Identity.CanonicalRepoPath)
            .Should().NotContain(p => p.EndsWith("repo0") || p.EndsWith("repo1"));
    }

    [Fact]
    public async Task RecordLaunchAsync_PreservesUserDisplaySides_NotIdentitySides()
    {
        // Caller may pass display sides that differ from the identity's
        // canonical sides (e.g. a user-typed alias). The service must
        // preserve display verbatim; identity drives dedup.
        var svc = new RecentContextsService(_path);
        var canonical = ContextIdentityFactory.Create(@"C:\repos\foo",
            new DiffSide.CommitIsh("main"), new DiffSide.WorkingTree());
        var displayLeft = new DiffSide.CommitIsh("Main"); // different casing
        var displayRight = new DiffSide.WorkingTree();

        await svc.RecordLaunchAsync(canonical, displayLeft, displayRight);

        svc.Current[0].LeftDisplay.Should().Be(displayLeft);
        ((DiffSide.CommitIsh)svc.Current[0].LeftDisplay).Reference.Should().Be("Main");
    }

    [Fact]
    public async Task RemoveAsync_DropsMatchingEntry()
    {
        var svc = new RecentContextsService(_path);
        var (id1, l1, r1) = MakeIdentity(@"C:\repos\foo", "main");
        var (id2, l2, r2) = MakeIdentity(@"C:\repos\bar", "main");
        await svc.RecordLaunchAsync(id1, l1, r1);
        await svc.RecordLaunchAsync(id2, l2, r2);

        await svc.RemoveAsync(id1);

        svc.Current.Should().ContainSingle()
            .Which.Identity.Should().Be(id2);
    }

    [Fact]
    public async Task RemoveAsync_UnknownIdentity_NoOp()
    {
        var svc = new RecentContextsService(_path);
        var (id, left, right) = MakeIdentity(@"C:\repos\foo", "main");
        await svc.RecordLaunchAsync(id, left, right);

        var unknown = ContextIdentityFactory.Create(@"C:\repos\nope",
            new DiffSide.CommitIsh("main"), new DiffSide.WorkingTree());
        await svc.RemoveAsync(unknown);

        svc.Current.Should().ContainSingle();
    }

    [Fact]
    public async Task RemoveAsync_RaisesChangedEvent_EvenWhenNoOp()
    {
        // Acceptable: the event fires whenever the snapshot is replaced,
        // regardless of whether content actually changed. UI consumers
        // re-render but don't visibly flicker.
        var svc = new RecentContextsService(_path);
        var fired = 0;
        svc.Changed += (_, _) => fired++;

        var unknown = ContextIdentityFactory.Create(@"C:\repos\nope",
            new DiffSide.CommitIsh("main"), new DiffSide.WorkingTree());
        await svc.RemoveAsync(unknown);

        fired.Should().Be(1);
    }

    [Fact]
    public async Task RecordLaunchAsync_NullSidesThrow()
    {
        var svc = new RecentContextsService(_path);
        var id = ContextIdentityFactory.Create(@"C:\repos\foo",
            new DiffSide.WorkingTree(), new DiffSide.WorkingTree());

        Func<Task> nullLeft = () => svc.RecordLaunchAsync(id, null!, new DiffSide.WorkingTree());
        Func<Task> nullRight = () => svc.RecordLaunchAsync(id, new DiffSide.WorkingTree(), null!);

        await nullLeft.Should().ThrowAsync<ArgumentNullException>();
        await nullRight.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ConcurrentRecord_FromSameInstance_NoLostUpdate()
    {
        var svc = new RecentContextsService(_path);
        var tasks = new List<Task>();
        for (var i = 0; i < 8; i++)
        {
            var (id, left, right) = MakeIdentity($@"C:\repos\repo{i}", "main");
            tasks.Add(Task.Run(() => svc.RecordLaunchAsync(id, left, right)));
        }
        await Task.WhenAll(tasks);

        svc.Current.Select(i => i.Identity.CanonicalRepoPath)
            .Should().OnlyHaveUniqueItems()
            .And.HaveCount(8);
    }

    [Fact]
    public async Task ConcurrentRecord_FromDifferentInstances_BothPersist()
    {
        // Simulates two DiffViewer processes recording at the same time.
        // RecentsStore's FileShare.None lock + the read-modify-write
        // primitive guarantee both end up on disk.
        var svc1 = new RecentContextsService(_path);
        var svc2 = new RecentContextsService(_path);

        var (id1, l1, r1) = MakeIdentity(@"C:\repos\one", "main");
        var (id2, l2, r2) = MakeIdentity(@"C:\repos\two", "main");

        await Task.WhenAll(
            svc1.RecordLaunchAsync(id1, l1, r1),
            svc2.RecordLaunchAsync(id2, l2, r2));

        // Each instance only sees what it merged with. To verify both
        // ended up on disk, hydrate a fresh instance.
        var observer = new RecentContextsService(_path);
        await observer.LoadAsync();
        observer.Current.Select(i => i.Identity.CanonicalRepoPath)
            .Should().BeEquivalentTo(new[]
            {
                ContextIdentityFactory.CanonicalizeRepoPath(@"C:\repos\one"),
                ContextIdentityFactory.CanonicalizeRepoPath(@"C:\repos\two"),
            });
    }

    private static RecentLaunchContext MakeContext(string repo, string leftRef, DateTimeOffset stamp)
    {
        var (id, left, right) = MakeIdentity(repo, leftRef);
        return new RecentLaunchContext(id, left, right, stamp);
    }

    private static (ContextIdentity id, DiffSide left, DiffSide right) MakeIdentity(string repo, string leftRef)
    {
        var left = new DiffSide.CommitIsh(leftRef);
        var right = new DiffSide.WorkingTree();
        return (ContextIdentityFactory.Create(repo, left, right), left, right);
    }
}
