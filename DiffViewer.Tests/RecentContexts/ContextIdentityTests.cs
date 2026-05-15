using System;
using DiffViewer.Models;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.RecentContexts;

public class ContextIdentityTests
{
    [Theory]
    [InlineData("C:\\Foo\\Bar\\", "C:\\Foo\\Bar")]
    [InlineData("C:\\Foo\\Bar", "C:\\Foo\\Bar")]
    [InlineData("C:\\Foo\\Bar\\\\", "C:\\Foo\\Bar")]
    public void CanonicalizeRepoPath_StripsTrailingSeparators(string input, string expected)
    {
        ContextIdentityFactory.CanonicalizeRepoPath(input)
            .Should().Be(expected);
    }

    [Fact]
    public void CanonicalizeRepoPath_ResolvesRelativeSegments()
    {
        var canonical = ContextIdentityFactory.CanonicalizeRepoPath("C:\\Foo\\Bar\\..\\Baz");
        canonical.Should().Be("C:\\Foo\\Baz");
    }

    [Fact]
    public void CanonicalizeRepoPath_NormalizesForwardSlashes()
    {
        // Path.GetFullPath on Windows replaces '/' with '\'.
        var canonical = ContextIdentityFactory.CanonicalizeRepoPath("C:/Foo/Bar");
        canonical.Should().Be("C:\\Foo\\Bar");
    }

    [Theory]
    [InlineData("C:\\Foo\\Bar", "C:\\foo\\bar")]
    [InlineData("C:\\Foo\\Bar\\", "c:/foo/bar")]
    [InlineData("C:\\Foo", "C:\\Foo\\..\\Foo")]
    public void RepoPathsEqual_TreatsCaseAndSeparatorAndRelativeAsEquivalent(string a, string b)
    {
        ContextIdentityFactory.RepoPathsEqual(a, b).Should().BeTrue();
    }

    [Theory]
    [InlineData("C:\\Foo", "C:\\Bar")]
    [InlineData("C:\\Foo\\Bar", "C:\\Foo\\Baz")]
    public void RepoPathsEqual_DistinguishesDifferentDirectories(string a, string b)
    {
        ContextIdentityFactory.RepoPathsEqual(a, b).Should().BeFalse();
    }

    [Fact]
    public void Create_EqualForCaseAndSeparatorVariants_OfTheSameDir()
    {
        var id1 = ContextIdentityFactory.Create("C:\\Foo\\Bar\\", new DiffSide.WorkingTree(), new DiffSide.CommitIsh("HEAD"));
        var id2 = ContextIdentityFactory.Create("C:/Foo/Bar", new DiffSide.WorkingTree(), new DiffSide.CommitIsh("HEAD"));

        id1.CanonicalRepoPath.Should().Be(id2.CanonicalRepoPath);
        id1.Should().Be(id2);
    }

    [Fact]
    public void Create_DistinguishesByDiffSide()
    {
        var path = "C:\\Foo";
        var idA = ContextIdentityFactory.Create(path, new DiffSide.WorkingTree(), new DiffSide.CommitIsh("HEAD"));
        var idB = ContextIdentityFactory.Create(path, new DiffSide.WorkingTree(), new DiffSide.CommitIsh("HEAD~1"));
        var idC = ContextIdentityFactory.Create(path, new DiffSide.CommitIsh("HEAD"), new DiffSide.WorkingTree());

        idA.Should().NotBe(idB);
        idA.Should().NotBe(idC);
        idB.Should().NotBe(idC);
    }

    [Fact]
    public void Create_DiffSideRecordEquality_TreatsCommitIshLiteralStringAsKey()
    {
        // CommitIsh references compare case-sensitively (positional record
        // equality) — Git treats some refspec contexts case-sensitively
        // and a literal-string match is the safer dedup rule.
        var id1 = ContextIdentityFactory.Create("C:\\Foo", new DiffSide.WorkingTree(), new DiffSide.CommitIsh("HEAD"));
        var id2 = ContextIdentityFactory.Create("C:\\Foo", new DiffSide.WorkingTree(), new DiffSide.CommitIsh("head"));

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void RecentLaunchContext_RecordEquality_ConsidersAllMembers()
    {
        var id = ContextIdentityFactory.Create("C:\\Foo", new DiffSide.WorkingTree(), new DiffSide.CommitIsh("HEAD"));
        var t = DateTimeOffset.UtcNow;

        var a = new RecentLaunchContext(id, new DiffSide.WorkingTree(), new DiffSide.CommitIsh("HEAD"), t);
        var b = new RecentLaunchContext(id, new DiffSide.WorkingTree(), new DiffSide.CommitIsh("HEAD"), t);
        a.Should().Be(b);

        // Different timestamp → different record.
        var c = new RecentLaunchContext(id, new DiffSide.WorkingTree(), new DiffSide.CommitIsh("HEAD"), t.AddSeconds(1));
        a.Should().NotBe(c);

        // Same identity, different display side → different record.
        var d = new RecentLaunchContext(id, new DiffSide.WorkingTree(), new DiffSide.CommitIsh("main"), t);
        a.Should().NotBe(d);
    }

    [Fact]
    public void CanonicalizeRepoPath_NullThrows()
    {
        var act = () => ContextIdentityFactory.CanonicalizeRepoPath(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
