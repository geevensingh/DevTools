using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

/// <summary>
/// Pure logic tests for <see cref="RepositoryEventDebouncer"/>. The class
/// has no FSW dependency so these tests run without spawning real watchers.
/// </summary>
public class RepositoryEventDebouncerTests
{
    private static readonly TimeSpan ShortInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan WaitForFire = TimeSpan.FromMilliseconds(300);

    [Fact]
    public void OnRawEvent_AfterDebounce_FiresOnce()
    {
        int fireCount = 0;
        RepositoryChangeKind capturedKind = RepositoryChangeKind.None;
        using var debouncer = new RepositoryEventDebouncer(ShortInterval, kind =>
        {
            Interlocked.Increment(ref fireCount);
            capturedKind = kind;
        });

        debouncer.OnRawEvent(RepositoryChangeKind.WorkingTree);

        SpinWaitFor(() => fireCount > 0, WaitForFire);

        fireCount.Should().Be(1);
        capturedKind.Should().Be(RepositoryChangeKind.WorkingTree);
    }

    [Fact]
    public void OnRawEvent_BurstWithinDebounceWindow_FiresOnce()
    {
        int fireCount = 0;
        using var debouncer = new RepositoryEventDebouncer(ShortInterval, _ => Interlocked.Increment(ref fireCount));

        for (int i = 0; i < 10; i++)
        {
            debouncer.OnRawEvent(RepositoryChangeKind.WorkingTree);
            Thread.Sleep(5); // shorter than the 50 ms debounce
        }

        SpinWaitFor(() => fireCount > 0, WaitForFire);

        fireCount.Should().Be(1);
    }

    [Fact]
    public void OnRawEvent_MixedKinds_AccumulatesIntoBitmask()
    {
        RepositoryChangeKind capturedKind = RepositoryChangeKind.None;
        var fired = new ManualResetEventSlim();
        using var debouncer = new RepositoryEventDebouncer(ShortInterval, kind =>
        {
            capturedKind = kind;
            fired.Set();
        });

        debouncer.OnRawEvent(RepositoryChangeKind.WorkingTree);
        debouncer.OnRawEvent(RepositoryChangeKind.GitDir);

        fired.Wait(WaitForFire).Should().BeTrue();
        capturedKind.Should().HaveFlag(RepositoryChangeKind.WorkingTree);
        capturedKind.Should().HaveFlag(RepositoryChangeKind.GitDir);
    }

    [Fact]
    public void OnRawEvent_BufferOverflow_FiresImmediatelyWithoutDebounce()
    {
        var fired = new ManualResetEventSlim();
        RepositoryChangeKind capturedKind = RepositoryChangeKind.None;
        using var debouncer = new RepositoryEventDebouncer(TimeSpan.FromSeconds(10), kind =>
        {
            capturedKind = kind;
            fired.Set();
        });

        debouncer.OnRawEvent(RepositoryChangeKind.BufferOverflow);

        // Should fire well before the 10-second debounce.
        fired.Wait(TimeSpan.FromMilliseconds(200)).Should().BeTrue();
        capturedKind.Should().HaveFlag(RepositoryChangeKind.BufferOverflow);
    }

    [Fact]
    public void Suspend_BlocksFireUntilResumed()
    {
        int fireCount = 0;
        using var debouncer = new RepositoryEventDebouncer(ShortInterval, _ => Interlocked.Increment(ref fireCount));

        var token = debouncer.Suspend();
        debouncer.OnRawEvent(RepositoryChangeKind.WorkingTree);
        Thread.Sleep(WaitForFire);
        fireCount.Should().Be(0);

        token.Dispose();
        // Resume fires synchronously when there's a pending event.
        fireCount.Should().Be(1);
    }

    [Fact]
    public void Suspend_NestedTokens_OnlyResumeOnOutermostDispose()
    {
        int fireCount = 0;
        using var debouncer = new RepositoryEventDebouncer(ShortInterval, _ => Interlocked.Increment(ref fireCount));

        var outer = debouncer.Suspend();
        var inner = debouncer.Suspend();

        debouncer.OnRawEvent(RepositoryChangeKind.WorkingTree);
        Thread.Sleep(WaitForFire);
        fireCount.Should().Be(0);

        inner.Dispose();
        Thread.Sleep(WaitForFire);
        fireCount.Should().Be(0); // still suspended

        outer.Dispose();
        fireCount.Should().Be(1);
    }

    [Fact]
    public void Suspend_NoPendingEvent_DoesNotFireOnResume()
    {
        int fireCount = 0;
        using var debouncer = new RepositoryEventDebouncer(ShortInterval, _ => Interlocked.Increment(ref fireCount));

        var token = debouncer.Suspend();
        token.Dispose();

        Thread.Sleep(WaitForFire);
        fireCount.Should().Be(0);
    }

    [Fact]
    public void Suspend_TokenDoubleDispose_IsSafe()
    {
        int fireCount = 0;
        using var debouncer = new RepositoryEventDebouncer(ShortInterval, _ => Interlocked.Increment(ref fireCount));

        var token = debouncer.Suspend();
        token.Dispose();
        token.Dispose(); // no-op

        debouncer.OnRawEvent(RepositoryChangeKind.WorkingTree);
        SpinWaitFor(() => fireCount > 0, WaitForFire);
        fireCount.Should().Be(1);
    }

    [Fact]
    public void Dispose_CancelsPendingFire()
    {
        int fireCount = 0;
        var debouncer = new RepositoryEventDebouncer(TimeSpan.FromMilliseconds(200),
            _ => Interlocked.Increment(ref fireCount));

        debouncer.OnRawEvent(RepositoryChangeKind.WorkingTree);
        debouncer.Dispose();

        Thread.Sleep(TimeSpan.FromMilliseconds(400));
        fireCount.Should().Be(0);
    }

    [Fact]
    public void OnRawEvent_None_IsNoOp()
    {
        int fireCount = 0;
        using var debouncer = new RepositoryEventDebouncer(ShortInterval, _ => Interlocked.Increment(ref fireCount));

        debouncer.OnRawEvent(RepositoryChangeKind.None);
        Thread.Sleep(WaitForFire);
        fireCount.Should().Be(0);
    }

    private static void SpinWaitFor(Func<bool> predicate, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (predicate()) return;
            Thread.Sleep(10);
        }
    }
}
