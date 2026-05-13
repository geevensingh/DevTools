using System.IO;
using System.Text;
using LibGit2Sharp;

namespace DiffViewer.Tests.Services;

/// <summary>
/// Helper that creates a real on-disk LibGit2Sharp repository in a temp folder
/// for the duration of a single test. Disposed via xUnit's IDisposable hook
/// in the test class.
/// </summary>
internal sealed class TempRepo : IDisposable
{
    private readonly string _tempPath;
    private readonly Signature _author = new("Test", "test@example.com", DateTimeOffset.UtcNow);

    public string Path => _tempPath;
    public Signature Author => _author;

    public TempRepo()
    {
        _tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "diffviewer-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempPath);
        Repository.Init(_tempPath);
    }

    public void WriteFile(string relativePath, string content, Encoding? encoding = null)
    {
        var full = System.IO.Path.Combine(_tempPath, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public void WriteBytes(string relativePath, byte[] bytes)
    {
        var full = System.IO.Path.Combine(_tempPath, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    public void DeleteWorkingFile(string relativePath)
    {
        var full = System.IO.Path.Combine(_tempPath, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        if (File.Exists(full)) File.Delete(full);
    }

    public void Stage(params string[] paths)
    {
        using var repo = new Repository(_tempPath);
        foreach (var p in paths)
        {
            repo.Index.Add(p);
        }
        repo.Index.Write();
    }

    public void Unstage(params string[] paths)
    {
        using var repo = new Repository(_tempPath);
        foreach (var p in paths)
        {
            repo.Index.Remove(p);
        }
        repo.Index.Write();
    }

    public Commit Commit(string message)
    {
        using var repo = new Repository(_tempPath);
        Commands.Stage(repo, "*");
        return repo.Commit(message, _author, _author, new CommitOptions { AllowEmptyCommit = true });
    }

    public Commit InitialCommit(string message = "init")
    {
        using var repo = new Repository(_tempPath);
        Commands.Stage(repo, "*");
        return repo.Commit(message, _author, _author);
    }

    public void Dispose()
    {
        try
        {
            // LibGit2Sharp marks .git internals read-only on Windows; clear before delete.
            if (Directory.Exists(_tempPath))
            {
                foreach (var f in Directory.EnumerateFiles(_tempPath, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                }
                Directory.Delete(_tempPath, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup - don't fail the test on temp-dir leak.
        }
    }
}
