using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using LibGit2Sharp;
using Xunit;

namespace DiffViewer.Tests.Services;

/// <summary>
/// End-to-end coverage for <see cref="GitWriteService"/>. Each test
/// builds a real fixture repo via <see cref="TempRepo"/>, invokes the
/// service against the user's installed <c>git.exe</c>, and asserts the
/// resulting index/working-tree state via LibGit2Sharp.
/// </summary>
public sealed class GitWriteServiceTests
{
    // ---------------- Whole-file ops ----------------

    [Fact]
    public async Task StageFile_PromotesUntrackedToStagedAdd()
    {
        using var t = new TempRepo();
        t.WriteFile("seed.txt", "seed\n");
        t.InitialCommit();

        t.WriteFile("new.txt", "hello\n");

        var svc = new GitWriteService();
        var result = await svc.StageFileAsync(t.Path, "new.txt");

        result.Success.Should().BeTrue(result.StdErr);
        using var repo = new Repository(t.Path);
        repo.Index["new.txt"].Should().NotBeNull("file should be tracked after `git add`");
    }

    [Fact]
    public async Task UnstageFile_RemovesStagedAdd()
    {
        using var t = new TempRepo();
        t.WriteFile("seed.txt", "seed\n");
        t.InitialCommit();

        t.WriteFile("new.txt", "hello\n");
        t.Stage("new.txt");

        var svc = new GitWriteService();
        var result = await svc.UnstageFileAsync(t.Path, "new.txt");

        result.Success.Should().BeTrue(result.StdErr);
        using var repo = new Repository(t.Path);
        repo.Index["new.txt"].Should().BeNull("staged add should be removed by `git restore --staged`");
        File.Exists(Path.Combine(t.Path, "new.txt")).Should().BeTrue("working-tree copy must survive unstage");
    }

    [Fact]
    public async Task BadGitArg_FailsWithStdErrSurfaced()
    {
        using var t = new TempRepo();
        t.WriteFile("seed.txt", "seed\n");
        t.InitialCommit();

        var svc = new GitWriteService();
        // Path that doesn't exist - git add should fail.
        var result = await svc.StageFileAsync(t.Path, "no-such-file.txt");

        result.Success.Should().BeFalse();
        result.ExitCode.Should().NotBe(0);
        result.StdErr.Should().NotBeNullOrWhiteSpace();
    }

    // ---------------- Hunk round-trip ----------------

    [Fact]
    public async Task StageHunk_ThenUnstageHunk_RoundTripsToOriginalState()
    {
        using var t = new TempRepo();
        t.WriteFile("file.txt", "alpha\nbravo\ncharlie\ndelta\necho\n");
        t.InitialCommit();

        // Edit the working tree.
        var modified = "alpha\nbravo CHANGED\ncharlie\ndelta\necho\n";
        t.WriteFile("file.txt", modified);

        var diffService = new DiffService();
        var hunks = diffService.ComputeDiff(
            "alpha\nbravo\ncharlie\ndelta\necho\n",
            modified,
            new DiffOptions(IgnoreWhitespace: false, ContextLines: 3, MaxLines: int.MaxValue, TimeoutMs: 5000)).Hunks;

        hunks.Should().HaveCount(1, "single-line edit should produce one hunk");

        var inputs = new HunkPatchInputs(
            FilePath: "file.txt",
            Hunk: hunks[0],
            LeftSource: "alpha\nbravo\ncharlie\ndelta\necho\n",
            RightSource: modified);

        var svc = new GitWriteService();

        // 1. Stage it.
        var stage = await svc.StageHunkAsync(t.Path, inputs);
        stage.Success.Should().BeTrue(stage.StdErr);

        using (var repo = new Repository(t.Path))
        {
            // After staging, the index entry's blob should match the modified content.
            var indexBlob = repo.Lookup<Blob>(repo.Index["file.txt"].Id);
            indexBlob.GetContentText().Should().Be(modified);
        }

        // 2. Reverse it. Index should now match HEAD again.
        var unstage = await svc.UnstageHunkAsync(t.Path, inputs);
        unstage.Success.Should().BeTrue(unstage.StdErr);

        using (var repo = new Repository(t.Path))
        {
            var headBlob = repo.Lookup<Blob>(repo.Head.Tip["file.txt"].Target.Id);
            var indexBlob = repo.Lookup<Blob>(repo.Index["file.txt"].Id);
            indexBlob.GetContentText().Should().Be(headBlob.GetContentText(),
                "unstaging the hunk we just staged should restore HEAD's blob");
        }
    }

    [Fact]
    public async Task RevertHunk_DropsUnstagedChangeFromWorkingTree()
    {
        using var t = new TempRepo();
        var original = "one\ntwo\nthree\nfour\nfive\n";
        t.WriteFile("file.txt", original);
        t.InitialCommit();

        var modified = "one\ntwo MODIFIED\nthree\nfour\nfive\n";
        t.WriteFile("file.txt", modified);

        var diffService = new DiffService();
        var hunks = diffService.ComputeDiff(
            original, modified,
            new DiffOptions(IgnoreWhitespace: false, ContextLines: 3, MaxLines: int.MaxValue, TimeoutMs: 5000)).Hunks;

        var svc = new GitWriteService();
        var result = await svc.RevertHunkAsync(t.Path, new HunkPatchInputs("file.txt", hunks[0], original, modified));

        result.Success.Should().BeTrue(result.StdErr);
        File.ReadAllText(Path.Combine(t.Path, "file.txt")).Should().Be(original);
    }

    // ---------------- AddToGitignore protocol ----------------

    [Fact]
    public void BuildGitignorePattern_ConvertsBackslashesToForwardSlashes()
    {
        GitWriteService.BuildGitignorePattern(@"src\bin\foo.txt").Should().Be("/src/bin/foo.txt");
    }

    [Fact]
    public void BuildGitignorePattern_AnchorsToRepoRoot()
    {
        GitWriteService.BuildGitignorePattern("foo.txt").Should().Be("/foo.txt");
        GitWriteService.BuildGitignorePattern("/foo.txt").Should().Be("/foo.txt", "leading slash should not be doubled");
    }

    [Fact]
    public void BuildGitignorePattern_EscapesGitignoreSpecialChars()
    {
        GitWriteService.BuildGitignorePattern("foo*.txt").Should().Be(@"/foo\*.txt");
        GitWriteService.BuildGitignorePattern("a[b].txt").Should().Be(@"/a\[b\].txt");
        GitWriteService.BuildGitignorePattern("!important.txt").Should().Be(@"/\!important.txt");
        GitWriteService.BuildGitignorePattern("#hash.txt").Should().Be(@"/\#hash.txt");
    }

    [Fact]
    public void BuildGitignorePattern_EscapesTrailingWhitespace()
    {
        GitWriteService.BuildGitignorePattern("foo.txt  ").Should().Be(@"/foo.txt\ \ ");
    }

    [Fact]
    public async Task AddToGitignore_CreatesFileWhenMissing()
    {
        using var t = new TempRepo();
        var svc = new GitWriteService();

        var result = await svc.AddToGitignoreAsync(t.Path, "build/output.log");

        result.Success.Should().BeTrue();
        var gitignore = File.ReadAllText(Path.Combine(t.Path, ".gitignore"));
        gitignore.Should().Be("/build/output.log\n");
    }

    [Fact]
    public async Task AddToGitignore_PreservesExistingContentAndAppendsLF()
    {
        using var t = new TempRepo();
        // Pre-existing file with no trailing newline.
        File.WriteAllBytes(Path.Combine(t.Path, ".gitignore"),
            System.Text.Encoding.UTF8.GetBytes("bin/\nobj/"));

        var svc = new GitWriteService();
        var result = await svc.AddToGitignoreAsync(t.Path, "logs/run.log");

        result.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(t.Path, ".gitignore"))
            .Should().Be("bin/\nobj/\n/logs/run.log\n");
    }

    [Fact]
    public async Task AddToGitignore_IsIdempotentOnRepeatedCalls()
    {
        using var t = new TempRepo();
        var svc = new GitWriteService();

        await svc.AddToGitignoreAsync(t.Path, "build/output.log");
        var afterFirst = File.ReadAllText(Path.Combine(t.Path, ".gitignore"));

        var second = await svc.AddToGitignoreAsync(t.Path, "build/output.log");
        var afterSecond = File.ReadAllText(Path.Combine(t.Path, ".gitignore"));

        second.Success.Should().BeTrue();
        second.StdOut.Should().Contain("already in .gitignore");
        afterSecond.Should().Be(afterFirst, "second add of the same pattern must be a no-op");
    }

    // ---------------- Recycle Bin ----------------

    [Fact]
    public async Task DeleteToRecycleBin_RemovesFileFromWorkingTree()
    {
        using var t = new TempRepo();
        t.WriteFile("trash.txt", "byebye\n");
        var fullPath = Path.Combine(t.Path, "trash.txt");
        File.Exists(fullPath).Should().BeTrue();

        var svc = new GitWriteService();
        var result = await svc.DeleteToRecycleBinAsync(t.Path, "trash.txt");

        result.Success.Should().BeTrue(result.StdErr);
        File.Exists(fullPath).Should().BeFalse("file should be moved to the Recycle Bin");
    }

    // ---------------- Watcher coordination events ----------------

    [Fact]
    public async Task BeforeAndAfterOperation_FireInOrderAroundStageFile()
    {
        using var t = new TempRepo();
        t.WriteFile("seed.txt", "x\n");
        t.InitialCommit();
        t.WriteFile("new.txt", "y\n");

        var svc = new GitWriteService();
        var events = new System.Collections.Generic.List<string>();
        svc.BeforeOperation += (_, e) => events.Add($"before:{e.Kind}:{e.FilePath}");
        svc.AfterOperation += (_, e) => events.Add($"after:{e.Kind}:{e.FilePath}:{e.Result?.Success}");

        await svc.StageFileAsync(t.Path, "new.txt");

        events.Should().Equal(
            "before:StageFile:new.txt",
            "after:StageFile:new.txt:True");
    }
}
