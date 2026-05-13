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
}
