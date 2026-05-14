using System.IO;
using DiffViewer.Services;
using LibGit2Sharp;

namespace DiffViewer;

/// <summary>
/// Production <see cref="ICommandLineEnvironment"/> backed by the real
/// process. Parser tests use <c>StubEnv</c> instead.
/// </summary>
internal sealed class ProcessCommandLineEnvironment : ICommandLineEnvironment
{
    public string CurrentDirectory => Directory.GetCurrentDirectory();

    public bool PathExists(string path) =>
        Directory.Exists(path) || File.Exists(path);

    public bool IsGitRepository(string path)
    {
        try
        {
            return Repository.IsValid(path);
        }
        catch
        {
            return false;
        }
    }

    public bool TryResolveCommitIsh(string repoPath, string commitIsh)
    {
        try
        {
            using var repo = new Repository(repoPath);
            return repo.Lookup<Commit>(commitIsh) is not null;
        }
        catch
        {
            return false;
        }
    }

    public string? TryDiscoverRepoRoot(string path)
    {
        try
        {
            // Repository.Discover walks upward looking for a .git dir (and
            // handles linked worktrees and submodules). Returns the .git
            // directory path (or the bare repo dir), trailing slash, or
            // null when nothing's found.
            var gitDir = Repository.Discover(path);
            if (string.IsNullOrEmpty(gitDir)) return null;

            using var repo = new Repository(gitDir);
            if (repo.Info.IsBare) return null;

            var workDir = repo.Info.WorkingDirectory;
            if (string.IsNullOrEmpty(workDir)) return null;

            return workDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
    }
}
