using System;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.Utility;
using DiffViewer.ViewModels;

namespace DiffViewer;

/// <summary>
/// Manual constructor-injection composition. No DI container — the service
/// graph is small and explicit wiring is easier to follow.
///
/// <para>Two callable seams (one for each phase of the launch):</para>
/// <list type="bullet">
///   <item><see cref="BuildArgs"/> turns command-line args into a
///         <see cref="ParsedCommandLine"/> (or an error). Pure; no IO
///         beyond what the parser does to discover the repo root.</item>
///   <item><see cref="BuildContextAsync"/> takes a parsed command line +
///         the App-level <see cref="AppServices"/> + a fresh
///         <see cref="ContextScope"/>, and produces a fully-loaded
///         <see cref="MainViewModel"/>. Per-context resources
///         (<see cref="IRepositoryService"/>, <see cref="IRepositoryWatcher"/>,
///         <see cref="IPreDiffPass"/>, <see cref="IGitWriteService"/>) are
///         registered with the scope so a single <c>scope.DisposeAsync()</c>
///         tears the entire per-context graph down in the right order.</item>
/// </list>
///
/// <para><b>Note on <see cref="IGitWriteService"/></b>: it is constructed
/// per-VM (kept inside the scope), <em>not</em> hoisted to
/// <see cref="AppServices"/>. Its <c>BeforeOperation</c>/<c>AfterOperation</c>
/// events fire <c>GitWriteOperationEventArgs</c> with no <c>RepoPath</c>
/// field, so <see cref="MainViewModel"/> relies on instance-identity to
/// disambiguate. Hoisting it would silently break that invariant.</para>
/// </summary>
internal static class CompositionRoot
{
    /// <summary>
    /// Parse + resolve the command line. The resulting
    /// <see cref="CommandLineParseResult"/> is either a successful
    /// <see cref="ParsedCommandLine"/> (with the repo root discovered and
    /// commit-ish references resolved) or a structured
    /// <see cref="CommandLineError"/>.
    /// </summary>
    public static CommandLineParseResult BuildArgs(System.Collections.Generic.IReadOnlyList<string> args)
    {
        var parser = new CommandLineParser();
        var env = new ProcessCommandLineEnvironment();
        return parser.Parse(args, env);
    }

    /// <summary>
    /// Build a fully-loaded <see cref="MainViewModel"/> for the supplied
    /// <see cref="ParsedCommandLine"/>. All per-context resources are
    /// registered with <paramref name="scope"/>; on success, calling
    /// <c>scope.DisposeAsync()</c> tears down the whole graph in reverse
    /// order. On failure (bare-repo guard violation, repo open exception)
    /// any partially-constructed resources are cleaned up via the same
    /// scope before the exception propagates.
    /// </summary>
    public static async Task<MainViewModel> BuildContextAsync(
        ParsedCommandLine parsed,
        AppServices services,
        ContextScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(scope);

        ct.ThrowIfCancellationRequested();

        IRepositoryService repo;
        try
        {
            repo = new RepositoryService(parsed.RepoPath);
        }
        catch (Exception ex)
        {
            throw new ContextBuildException($"Failed to open repository: {ex.Message}", ex);
        }
        scope.Register(repo);

        // Bare-repo guard: working-tree input modes are rejected. Throw
        // ContextBuildException so the caller can render a friendly
        // message; scope.DisposeAsync() (already wired) will clean up
        // the half-built graph.
        if (repo.Shape.IsBare && (parsed.Left is DiffSide.WorkingTree || parsed.Right is DiffSide.WorkingTree))
        {
            throw new ContextBuildException(
                $"Bare repository at `{repo.Shape.RepoRoot}` has no working tree. " +
                "Only commit-vs-commit comparisons are supported.");
        }

        // Watcher only when at least one side is the working tree.
        IRepositoryWatcher? watcher = null;
        bool isCommitVsCommit = parsed.Left is DiffSide.CommitIsh && parsed.Right is DiffSide.CommitIsh;
        if (!isCommitVsCommit && repo.Shape.WorkingDirectory is { } workingDir)
        {
            try
            {
                watcher = new RepositoryWatcher(
                    workingDir,
                    repo.Shape.GitDir,
                    repo.IsPathIgnored);
                scope.Register(watcher);
            }
            catch
            {
                // FSW construction failed (rare; e.g. permissions) — fall
                // back to no live updates rather than refusing to launch.
                watcher = null;
            }
        }

        var preDiffPass = new PreDiffPass(
            repo, services.DiffService,
            maxConcurrency: PreDiffPass.DefaultMaxConcurrency,
            getLargeFileThresholdBytes: () => services.SettingsService.Current.LargeFileThresholdBytes);
        scope.Register(preDiffPass);

        // GitWriteService is per-VM by design (event-args identity
        // contract — see class header).
        var gitWriteService = new GitWriteService();

        var vm = new MainViewModel(
            repo, parsed.Left, parsed.Right,
            services.DiffService, watcher,
            preDiffPass: preDiffPass,
            settingsService: services.SettingsService,
            gitWriteService: gitWriteService,
            externalAppLauncher: services.ExternalAppLauncher,
            scope: scope);

        await vm.LoadInitialChangesAsync(ct).ConfigureAwait(true);
        return vm;
    }
}

/// <summary>
/// Thrown by <see cref="CompositionRoot.BuildContextAsync"/> when a
/// per-context build cannot complete (bare-repo guard violation, repo
/// open failure, etc.). <see cref="Exception.Message"/> is intended for
/// direct user display.
/// </summary>
internal sealed class ContextBuildException : Exception
{
    public ContextBuildException(string message) : base(message) { }
    public ContextBuildException(string message, Exception inner) : base(message, inner) { }
}
