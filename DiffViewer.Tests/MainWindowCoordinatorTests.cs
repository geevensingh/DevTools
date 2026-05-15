using System.Threading.Tasks;
using DiffViewer;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.Tests.Services;
using DiffViewer.Utility;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests;

/// <summary>
/// Coordinator-level tests that exercise the swap / dispose / record
/// pipeline against real <see cref="TempRepo"/>-backed MainViewModels.
/// Heavier than unit tests but the only way to catch regressions in the
/// "outgoing-disposed-AFTER-swap" invariant.
/// </summary>
public class MainWindowCoordinatorTests
{
    [Fact]
    public async Task InitialLaunchAsync_ParseFailure_ShowsErrorAndShutsDown()
    {
        var dialog = new FakeDialog();
        int? exitCode = null;
        var services = BuildServices(out _);

        var coordinator = new MainWindowCoordinator(
            services, dialog, default, shutdownAction: c => exitCode = c);

        // Two positional args > grammar limit → parse fails.
        var ok = await coordinator.InitialLaunchAsync(new[] { "C:\\nope1", "C:\\nope2", "C:\\nope3" });

        ok.Should().BeFalse();
        dialog.LastError.Should().NotBeNull();
        exitCode.Should().Be(1);
        coordinator.Current.Should().BeNull();
    }

    [Fact]
    public async Task StartFromParsedAsync_Success_SetsCurrentAndRecords()
    {
        using var repo = MakeRepoWithCommit();
        var services = BuildServices(out var recents);
        var dialog = new FakeDialog();
        var coordinator = new MainWindowCoordinator(services, dialog, shutdownAction: _ => { });

        var parsed = ParsedFor(repo);
        var ok = await coordinator.StartFromParsedAsync(parsed);

        ok.Should().BeTrue();
        coordinator.Current.Should().NotBeNull();
        recents.RecordedRepoPaths.Should().ContainSingle().Which.Should().Be(repo.Path);

        await coordinator.DisposeCurrentAsync();
    }

    [Fact]
    public async Task SwitchContextAsync_Success_DisposesOutgoing_AFTER_Swap()
    {
        using var repoA = MakeRepoWithCommit();
        using var repoB = MakeRepoWithCommit();
        var services = BuildServices(out _);
        var dialog = new FakeDialog();
        var coordinator = new MainWindowCoordinator(services, dialog, shutdownAction: _ => { });

        (await coordinator.StartFromParsedAsync(ParsedFor(repoA))).Should().BeTrue();
        var firstScope = coordinator.CurrentScope!;
        var firstVm = coordinator.Current!;

        // Capture the dispose-state of the outgoing scope at the moment
        // CurrentChanged fires for the new VM.
        bool? firstScopeDisposedAtSwap = null;
        coordinator.CurrentChanged += (_, _) =>
        {
            if (!ReferenceEquals(coordinator.CurrentScope, firstScope) && firstScopeDisposedAtSwap is null)
            {
                firstScopeDisposedAtSwap = firstScope.IsDisposed;
            }
        };

        (await coordinator.SwitchContextAsync(ParsedFor(repoB))).Should().BeTrue();

        firstScopeDisposedAtSwap.Should().Be(false,
            "outgoing scope must still be alive when Current transitions to the new VM");
        firstScope.IsDisposed.Should().BeTrue("outgoing scope must be disposed after the switch completes");
        coordinator.Current.Should().NotBeNull().And.NotBeSameAs(firstVm);

        await coordinator.DisposeCurrentAsync();
    }

    [Fact]
    public async Task SwitchContextAsync_BuildFailure_DoesNotSwap_AndOffersRemove()
    {
        using var repoA = MakeRepoWithCommit();
        var services = BuildServices(out var recents);
        var dialog = new FakeDialog { ConfirmRemoveResult = true };
        var coordinator = new MainWindowCoordinator(services, dialog, shutdownAction: _ => { });

        (await coordinator.StartFromParsedAsync(ParsedFor(repoA))).Should().BeTrue();
        var stableScope = coordinator.CurrentScope;
        var stableVm = coordinator.Current;

        // Point at a non-existent path so RepositoryService throws.
        var badParsed = new ParsedCommandLine(
            @"C:\definitely-not-a-real-repo-" + System.Guid.NewGuid().ToString("N"),
            new DiffSide.WorkingTree(),
            new DiffSide.CommitIsh("HEAD"));
        var ok = await coordinator.SwitchContextAsync(badParsed);

        ok.Should().BeFalse();
        coordinator.Current.Should().BeSameAs(stableVm, "build failure must leave current VM untouched");
        coordinator.CurrentScope.Should().BeSameAs(stableScope);
        dialog.ConfirmRemoveCallCount.Should().Be(1);
        recents.RemovedRepoPaths.Should().ContainSingle().Which.Should().Be(badParsed.RepoPath);

        await coordinator.DisposeCurrentAsync();
    }

    [Fact]
    public async Task SwitchContextAsync_BuildFailure_NoRemove_WhenUserDeclines()
    {
        using var repoA = MakeRepoWithCommit();
        var services = BuildServices(out var recents);
        var dialog = new FakeDialog { ConfirmRemoveResult = false };
        var coordinator = new MainWindowCoordinator(services, dialog, shutdownAction: _ => { });

        (await coordinator.StartFromParsedAsync(ParsedFor(repoA))).Should().BeTrue();

        var badParsed = new ParsedCommandLine(
            @"C:\definitely-not-a-real-repo-" + System.Guid.NewGuid().ToString("N"),
            new DiffSide.WorkingTree(),
            new DiffSide.CommitIsh("HEAD"));
        await coordinator.SwitchContextAsync(badParsed);

        recents.RemovedRepoPaths.Should().BeEmpty();

        await coordinator.DisposeCurrentAsync();
    }

    private static AppServices BuildServices(out FakeRecents recents)
    {
        recents = new FakeRecents();
        return new AppServices(
            new SettingsService(),
            new DiffService(),
            new ExternalAppLauncher(null),
            recents);
    }

    private static TempRepo MakeRepoWithCommit()
    {
        var repo = new TempRepo();
        repo.WriteFile("hello.txt", "hello\n");
        repo.InitialCommit();
        return repo;
    }

    private static ParsedCommandLine ParsedFor(TempRepo repo) =>
        new(repo.Path, new DiffSide.WorkingTree(), new DiffSide.CommitIsh("HEAD"));

    private sealed class FakeDialog : IDialogService
    {
        public string? LastError { get; private set; }
        public int ConfirmRemoveCallCount { get; private set; }
        public bool ConfirmRemoveResult { get; set; } = true;

        public void ShowError(string title, string message) => LastError = message;
        public bool ConfirmRemoveStaleEntry(string repoPath, string error)
        {
            ConfirmRemoveCallCount++;
            return ConfirmRemoveResult;
        }
    }

    private sealed class FakeRecents : IRecentContextsService
    {
        public System.Collections.Generic.IReadOnlyList<RecentLaunchContext> Current { get; } =
            System.Array.Empty<RecentLaunchContext>();
        public event System.EventHandler? Changed { add { } remove { } }
        public System.Collections.Generic.List<string> RecordedRepoPaths { get; } = new();
        public System.Collections.Generic.List<string> RemovedRepoPaths { get; } = new();

        public Task RecordLaunchAsync(ContextIdentity identity, DiffSide leftDisplay, DiffSide rightDisplay, System.Threading.CancellationToken ct = default)
        {
            RecordedRepoPaths.Add(identity.CanonicalRepoPath);
            return Task.CompletedTask;
        }
        public Task RemoveAsync(ContextIdentity identity, System.Threading.CancellationToken ct = default)
        {
            RemovedRepoPaths.Add(identity.CanonicalRepoPath);
            return Task.CompletedTask;
        }
    }
}
