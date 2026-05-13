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
/// git repo, and whether a string resolves as a commit-ish in a given repo.
/// All four are injected so tests can stub them.
/// </summary>
public interface ICommandLineEnvironment
{
    string CurrentDirectory { get; }
    bool PathExists(string path);
    bool IsGitRepository(string path);
    bool TryResolveCommitIsh(string repoPath, string commitIsh);
}
