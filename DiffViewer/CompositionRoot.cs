using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.ViewModels;

namespace DiffViewer;

/// <summary>
/// Manual constructor-injection composition. No DI container in v1 - the
/// service graph is small and explicit wiring is easier to follow.
/// </summary>
internal static class CompositionRoot
{
    public static MainViewModel? BuildMainViewModel(IReadOnlyList<string> args, out string? error)
    {
        var parser = new CommandLineParser();
        var env = new ProcessCommandLineEnvironment();
        var parseResult = parser.Parse(args, env);

        if (!parseResult.IsSuccess)
        {
            error = parseResult.Error?.Message ?? "Unknown command-line error.";
            return null;
        }

        var parsed = parseResult.Parsed!;
        IRepositoryService repo;
        try
        {
            repo = new RepositoryService(parsed.RepoPath);
        }
        catch (Exception ex)
        {
            error = $"Failed to open repository: {ex.Message}";
            return null;
        }

        // Bare-repo guard: working-tree input modes are rejected.
        if (repo.Shape.IsBare && (parsed.Left is DiffSide.WorkingTree || parsed.Right is DiffSide.WorkingTree))
        {
            repo.Dispose();
            error = $"Bare repository at `{repo.Shape.RepoRoot}` has no working tree. " +
                    "Only commit-vs-commit comparisons are supported.";
            return null;
        }

        error = null;
        var diffService = new DiffService();

        // Construct the watcher only when at least one side is the working
        // tree. Commit-vs-commit comparisons can't change.
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
            }
            catch
            {
                // If FSW construction fails (rare; e.g. permissions), fall
                // back to no live updates rather than refusing to launch.
                watcher = null;
            }
        }

        var vm = new MainViewModel(repo, parsed.Left, parsed.Right, diffService, watcher);
        vm.LoadInitialChanges();
        return vm;
    }
}
