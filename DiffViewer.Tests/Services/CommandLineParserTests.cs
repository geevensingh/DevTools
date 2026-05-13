using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

public class CommandLineParserTests
{
    private const string Cwd = @"C:\Repos\foo";
    private const string OtherRepo = @"C:\Repos\bar";

    /// <summary>Stub environment that lets each test choose what exists / what resolves.</summary>
    private sealed class StubEnv : ICommandLineEnvironment
    {
        public string CurrentDirectory { get; init; } = Cwd;
        public HashSet<string> ExistingPaths { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> GitRepos { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Map: (repoPath, commitIsh) → resolves?</summary>
        public Dictionary<(string repo, string commit), bool> Commits { get; init; }
            = new(new TupleIgnoreCase());

        public bool PathExists(string path) => ExistingPaths.Contains(path);
        public bool IsGitRepository(string path) => GitRepos.Contains(path);
        public bool TryResolveCommitIsh(string repoPath, string commitIsh)
            => Commits.TryGetValue((repoPath, commitIsh), out var ok) && ok;

        private sealed class TupleIgnoreCase : IEqualityComparer<(string repo, string commit)>
        {
            public bool Equals((string repo, string commit) x, (string repo, string commit) y)
                => string.Equals(x.repo, y.repo, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(x.commit, y.commit, StringComparison.Ordinal);

            public int GetHashCode((string repo, string commit) obj)
                => HashCode.Combine(
                    obj.repo.ToLowerInvariant(),
                    obj.commit);
        }
    }

    private static StubEnv RepoOnlyEnv() => new()
    {
        GitRepos = { Cwd },
    };

    [Fact]
    public void NoArgs_InsideRepo_ProducesWorkingTreeVsHead()
    {
        var parser = new CommandLineParser();
        var env = RepoOnlyEnv();

        var result = parser.Parse(Array.Empty<string>(), env);

        result.IsSuccess.Should().BeTrue();
        result.Parsed!.RepoPath.Should().Be(Cwd);
        result.Parsed.Left.Should().BeOfType<DiffSide.CommitIsh>()
            .Which.Reference.Should().Be("HEAD");
        result.Parsed.Right.Should().BeOfType<DiffSide.WorkingTree>();
    }

    [Fact]
    public void NoArgs_NotARepo_Fails()
    {
        var parser = new CommandLineParser();
        var env = new StubEnv();

        var result = parser.Parse(Array.Empty<string>(), env);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(CommandLineErrorKind.NotAGitRepository);
    }

    [Fact]
    public void RepoPathOnly_ProducesWorkingTreeVsHead()
    {
        var parser = new CommandLineParser();
        var env = new StubEnv
        {
            ExistingPaths = { OtherRepo },
            GitRepos = { OtherRepo, Cwd },
        };

        var result = parser.Parse(new[] { OtherRepo }, env);

        result.IsSuccess.Should().BeTrue();
        result.Parsed!.RepoPath.Should().Be(OtherRepo);
        result.Parsed.Left.Should().BeOfType<DiffSide.CommitIsh>()
            .Which.Reference.Should().Be("HEAD");
        result.Parsed.Right.Should().BeOfType<DiffSide.WorkingTree>();
    }

    [Fact]
    public void CommitOnly_InsideRepo_ProducesWorkingTreeVsCommit()
    {
        var parser = new CommandLineParser();
        var env = new StubEnv
        {
            GitRepos = { Cwd },
            Commits = { [(Cwd, "main")] = true },
        };

        var result = parser.Parse(new[] { "main" }, env);

        result.IsSuccess.Should().BeTrue();
        result.Parsed!.RepoPath.Should().Be(Cwd);
        result.Parsed.Left.Should().BeOfType<DiffSide.CommitIsh>()
            .Which.Reference.Should().Be("main");
        result.Parsed.Right.Should().BeOfType<DiffSide.WorkingTree>();
    }

    [Fact]
    public void RepoPathAndCommit_ProducesWorkingTreeVsCommit()
    {
        var parser = new CommandLineParser();
        var env = new StubEnv
        {
            ExistingPaths = { OtherRepo },
            GitRepos = { OtherRepo, Cwd },
            Commits = { [(OtherRepo, "v1.2.3")] = true },
        };

        var result = parser.Parse(new[] { OtherRepo, "v1.2.3" }, env);

        result.IsSuccess.Should().BeTrue();
        result.Parsed!.RepoPath.Should().Be(OtherRepo);
        result.Parsed.Left.Should().BeOfType<DiffSide.CommitIsh>()
            .Which.Reference.Should().Be("v1.2.3");
        result.Parsed.Right.Should().BeOfType<DiffSide.WorkingTree>();
    }

    [Fact]
    public void TwoCommits_InsideRepo_ProducesCommitVsCommit()
    {
        var parser = new CommandLineParser();
        var env = new StubEnv
        {
            GitRepos = { Cwd },
            Commits =
            {
                [(Cwd, "HEAD~3")] = true,
                [(Cwd, "HEAD")] = true,
            },
        };

        var result = parser.Parse(new[] { "HEAD~3", "HEAD" }, env);

        result.IsSuccess.Should().BeTrue();
        result.Parsed!.RepoPath.Should().Be(Cwd);
        result.Parsed.Left.Should().BeOfType<DiffSide.CommitIsh>()
            .Which.Reference.Should().Be("HEAD~3");
        result.Parsed.Right.Should().BeOfType<DiffSide.CommitIsh>()
            .Which.Reference.Should().Be("HEAD");
    }

    [Fact]
    public void RepoPathAndTwoCommits_ProducesCommitVsCommit()
    {
        var parser = new CommandLineParser();
        var env = new StubEnv
        {
            ExistingPaths = { OtherRepo },
            GitRepos = { OtherRepo, Cwd },
            Commits =
            {
                [(OtherRepo, "abc1234")] = true,
                [(OtherRepo, "def5678")] = true,
            },
        };

        var result = parser.Parse(new[] { OtherRepo, "abc1234", "def5678" }, env);

        result.IsSuccess.Should().BeTrue();
        result.Parsed!.RepoPath.Should().Be(OtherRepo);
        result.Parsed.Left.Should().BeOfType<DiffSide.CommitIsh>()
            .Which.Reference.Should().Be("abc1234");
        result.Parsed.Right.Should().BeOfType<DiffSide.CommitIsh>()
            .Which.Reference.Should().Be("def5678");
    }

    [Fact]
    public void TooManyArgs_Fails()
    {
        var parser = new CommandLineParser();
        var env = RepoOnlyEnv();

        var result = parser.Parse(new[] { "a", "b", "c", "d" }, env);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(CommandLineErrorKind.TooManyArguments);
    }

    [Fact]
    public void UnknownFlag_Fails()
    {
        var parser = new CommandLineParser();
        var env = RepoOnlyEnv();

        var result = parser.Parse(new[] { "--bogus" }, env);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(CommandLineErrorKind.UnknownFlag);
    }

    [Fact]
    public void PathLikeArgThatDoesNotExist_FailsWithPathError()
    {
        var parser = new CommandLineParser();
        var env = RepoOnlyEnv();

        var result = parser.Parse(new[] { @"C:\does-not-exist" }, env);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(CommandLineErrorKind.PathDoesNotExist);
    }

    [Fact]
    public void PathLikeArgThatExistsButIsNotARepo_FailsWithRepoError()
    {
        var parser = new CommandLineParser();
        var env = new StubEnv
        {
            ExistingPaths = { @"C:\Repos\not-a-repo" },
            GitRepos = { Cwd },
        };

        var result = parser.Parse(new[] { @"C:\Repos\not-a-repo" }, env);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(CommandLineErrorKind.NotAGitRepository);
    }

    [Fact]
    public void CommitIshThatDoesNotResolve_Fails()
    {
        var parser = new CommandLineParser();
        var env = new StubEnv
        {
            GitRepos = { Cwd },
            // "ghost" deliberately not registered → TryResolveCommitIsh returns false.
        };

        var result = parser.Parse(new[] { "ghost" }, env);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(CommandLineErrorKind.UnknownCommitIsh);
    }

    [Fact]
    public void TwoCommits_SecondUnresolvable_Fails()
    {
        var parser = new CommandLineParser();
        var env = new StubEnv
        {
            GitRepos = { Cwd },
            Commits = { [(Cwd, "main")] = true },
        };

        var result = parser.Parse(new[] { "main", "ghost" }, env);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(CommandLineErrorKind.UnknownCommitIsh);
    }

    [Theory]
    [InlineData("./foo")]
    [InlineData(".\\foo")]
    [InlineData("../foo")]
    [InlineData("..\\foo")]
    [InlineData(@"\\server\share\foo")]
    [InlineData(@"D:\some\path")]
    public void RelativeOrUncOrDriveLetterPathThatDoesNotExist_FailsAsPath(string arg)
    {
        var parser = new CommandLineParser();
        var env = RepoOnlyEnv();

        var result = parser.Parse(new[] { arg }, env);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(CommandLineErrorKind.PathDoesNotExist);
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("HEAD~3")]
    [InlineData("main")]
    [InlineData("v1.2.3")]
    [InlineData("abc1234")]
    [InlineData("origin/main")]
    public void NonPathLikeArg_TreatedAsCommitIsh(string commit)
    {
        var parser = new CommandLineParser();
        var env = new StubEnv
        {
            GitRepos = { Cwd },
            Commits = { [(Cwd, commit)] = true },
        };

        var result = parser.Parse(new[] { commit }, env);

        result.IsSuccess.Should().BeTrue();
        result.Parsed!.Left.Should().BeOfType<DiffSide.CommitIsh>()
            .Which.Reference.Should().Be(commit);
    }
}
