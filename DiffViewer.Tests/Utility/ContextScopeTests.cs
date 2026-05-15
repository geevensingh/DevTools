using System;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Utility;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Utility;

public class ContextScopeTests
{
    [Fact]
    public async Task DisposeAsync_RunsDisposers_InReverseRegistrationOrder()
    {
        var order = new System.Collections.Generic.List<int>();
        var scope = new ContextScope();
        scope.RegisterCleanup(() => order.Add(1));
        scope.RegisterCleanup(() => order.Add(2));
        scope.RegisterCleanup(() => order.Add(3));

        await scope.DisposeAsync();

        order.Should().Equal(3, 2, 1);
    }

    [Fact]
    public async Task DisposeAsync_CancelsToken()
    {
        var scope = new ContextScope();
        var token = scope.Token;
        token.IsCancellationRequested.Should().BeFalse();

        await scope.DisposeAsync();

        token.IsCancellationRequested.Should().BeTrue();
        scope.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_AwaitsTrackedInFlightTasks_ThatObserveToken()
    {
        var scope = new ContextScope();
        var stopped = false;

        var task = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, scope.Token);
            }
            catch (OperationCanceledException)
            {
                // observed cancellation
            }
            stopped = true;
        });

        scope.TrackInFlight(task);

        await scope.DisposeAsync();

        stopped.Should().BeTrue("DisposeAsync should have awaited the in-flight task to drain");
    }

    [Fact]
    public async Task DisposeAsync_TimesOutOnUncooperativeInFlightTasks()
    {
        var scope = new ContextScope();
        var release = new TaskCompletionSource();

        // Task that ignores the cancellation token entirely. DisposeAsync
        // should not hang forever; it should hit the internal drain timeout
        // and proceed to disposers.
        scope.TrackInFlight(release.Task);

        var disposeStarted = DateTime.UtcNow;
        await scope.DisposeAsync();
        var elapsed = DateTime.UtcNow - disposeStarted;

        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15),
            "DisposeAsync must time out drain instead of waiting forever");

        // Release the dangling task so the test runner doesn't leak.
        release.TrySetResult();
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        int disposerCalls = 0;
        var scope = new ContextScope();
        scope.RegisterCleanup(() => disposerCalls++);

        await scope.DisposeAsync();
        await scope.DisposeAsync();
        await scope.DisposeAsync();

        disposerCalls.Should().Be(1, "registered disposers should run exactly once");
    }

    [Fact]
    public async Task LinkedAppShutdownToken_CancelsScopeToken()
    {
        using var appCts = new CancellationTokenSource();
        var scope = new ContextScope(appCts.Token);

        scope.Token.IsCancellationRequested.Should().BeFalse();
        appCts.Cancel();
        scope.Token.IsCancellationRequested.Should().BeTrue();

        await scope.DisposeAsync();
    }

    [Fact]
    public async Task Register_DisposableResource_IsDisposedDuringTeardown()
    {
        var resource = new DisposableProbe();
        var scope = new ContextScope();
        scope.Register(resource);

        await scope.DisposeAsync();

        resource.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Register_AsyncDisposableResource_IsDisposedDuringTeardown()
    {
        var resource = new AsyncDisposableProbe();
        var scope = new ContextScope();
        scope.Register(resource);

        await scope.DisposeAsync();

        resource.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_ContinuesEvenIfADisposerThrows()
    {
        var laterRan = false;
        var scope = new ContextScope();
        scope.RegisterCleanup(() => laterRan = true);
        scope.RegisterCleanup(() => throw new InvalidOperationException("intentional"));

        await scope.DisposeAsync();

        laterRan.Should().BeTrue("a single throwing disposer must not prevent other disposers from running");
    }

    private sealed class DisposableProbe : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    private sealed class AsyncDisposableProbe : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return default;
        }
    }
}
