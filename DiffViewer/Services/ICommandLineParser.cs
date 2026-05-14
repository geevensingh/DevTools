using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Resolves <c>DiffViewer</c>'s argv into a <see cref="ParsedCommandLine"/>.
/// Pure model layer — does not touch git directly. The disambiguation between
/// "this argument is a repo path" and "this argument is a commit-ish" is
/// delegated to a host-supplied <see cref="ICommandLineEnvironment"/> so the
/// parser stays unit-testable without spinning up LibGit2Sharp.
/// </summary>
public interface ICommandLineParser
{
    CommandLineParseResult Parse(IReadOnlyList<string> args, ICommandLineEnvironment env);
}

/// <summary>
/// Side-effecty bits the parser needs to ask the world about — current
/// working directory, whether a path exists on disk, whether a directory is a
/// git repo, whether a string resolves as a commit-ish in a given repo, and
/// how to walk upward from a subdirectory to find an enclosing repo root.
/// All five are injected so tests can stub them.
/// </summary>
public interface ICommandLineEnvironment
{
    string CurrentDirectory { get; }
    bool PathExists(string path);
    bool IsGitRepository(string path);
    bool TryResolveCommitIsh(string repoPath, string commitIsh);

    /// <summary>
    /// Walks upward from <paramref name="path"/> looking for an enclosing
    /// (non-bare) git repository. Returns the working-tree root if one is
    /// found, or <c>null</c> when there is no repo on the way up. Used to
    /// make the app work when launched from a subdirectory of a repo.
    /// </summary>
    string? TryDiscoverRepoRoot(string path);
}
