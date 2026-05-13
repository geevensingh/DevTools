using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using LibGit2Sharp;
using System.IO;
using Xunit;
using FileStatus = DiffViewer.Models.FileStatus;

namespace DiffViewer.Tests.Services;

public class RepositoryServiceTests
{
    [Fact]
    public void OpenInvalidPath_Throws()
    {
        var bogus = Path.Combine(Path.GetTempPath(), "diffviewer-not-a-repo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bogus);
        try
        {
            Action open = () => new RepositoryService(bogus);
            open.Should().Throw<ArgumentException>();
        }
        finally
        {
            Directory.Delete(bogus);
        }
    }

    [Fact]
    public void Shape_DetectsFreshRepoState()
    {
        using var t = new TempRepo();
        using var svc = new RepositoryService(t.Path);

        svc.Shape.IsBare.Should().BeFalse();
        svc.Shape.IsHeadUnborn.Should().BeTrue();
        svc.Shape.HasInProgressOperation.Should().BeFalse();
        svc.Shape.WorkingDirectory.Should().NotBeNull();
    }

    [Fact]
    public void EnumerateChanges_CommitVsCommit_ReturnsAddedDeletedModified()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "alpha\n");
        t.WriteFile("b.txt", "beta\n");
        var c1 = t.InitialCommit("c1");

        t.WriteFile("a.txt", "alpha CHANGED\n");
        t.DeleteWorkingFile("b.txt");
        t.WriteFile("c.txt", "gamma\n");
        var c2 = t.Commit("c2");

        using var svc = new RepositoryService(t.Path);

        var changes = svc.EnumerateChanges(
            new DiffSide.CommitIsh(c1.Sha),
            new DiffSide.CommitIsh(c2.Sha));

        changes.Should().HaveCount(3);
        changes.Should().ContainSingle(x => x.Path == "a.txt" && x.Status == FileStatus.Modified);
        changes.Should().ContainSingle(x => x.Path == "b.txt" && x.Status == FileStatus.Deleted);
        changes.Should().ContainSingle(x => x.Path == "c.txt" && x.Status == FileStatus.Added);

        // All commit-vs-commit changes get layer = None.
        changes.Should().OnlyContain(x => x.Layer == WorkingTreeLayer.None);
    }

    [Fact]
    public void EnumerateChanges_WorkingTreeVsHead_BucketsByLayer()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "alpha\n");
        t.WriteFile("b.txt", "beta\n");
        t.InitialCommit("c1");

        // Staged: edit + stage a.txt
        t.WriteFile("a.txt", "alpha STAGED\n");
        t.Stage("a.txt");

        // Unstaged: edit b.txt without staging
        t.WriteFile("b.txt", "beta UNSTAGED\n");

        // Untracked: brand-new file
        t.WriteFile("untracked.txt", "I'm new\n");

        using var svc = new RepositoryService(t.Path);
        var changes = svc.EnumerateChanges(
            new DiffSide.CommitIsh("HEAD"),
            new DiffSide.WorkingTree());

        changes.Should().Contain(x => x.Path == "a.txt" && x.Layer == WorkingTreeLayer.Staged);
        changes.Should().Contain(x => x.Path == "b.txt" && x.Layer == WorkingTreeLayer.Unstaged);
        changes.Should().Contain(x => x.Path == "untracked.txt" && x.Layer == WorkingTreeLayer.Untracked);
        changes.Should().NotContain(x => x.Layer == WorkingTreeLayer.CommittedSinceCommit);
    }

    [Fact]
    public void EnumerateChanges_WorkingTreeVsOlderCommit_IncludesCommittedSinceLayer()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "alpha\n");
        var c1 = t.InitialCommit("c1");

        t.WriteFile("a.txt", "alpha v2\n");
        t.Commit("c2");

        // Now WT vs c1 should include CommittedSinceCommit (the c1->c2 change of a.txt).
        using var svc = new RepositoryService(t.Path);
        var changes = svc.EnumerateChanges(
            new DiffSide.CommitIsh(c1.Sha),
            new DiffSide.WorkingTree());

        changes.Should().Contain(x => x.Path == "a.txt" && x.Layer == WorkingTreeLayer.CommittedSinceCommit);
    }

    [Fact]
    public void EnumerateChanges_EmittedRowsAppearInSameOrderAsLayerPrecedence()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        t.InitialCommit("c1");

        t.WriteFile("a.txt", "a staged\n");
        t.Stage("a.txt");
        t.WriteFile("a.txt", "a unstaged\n");
        t.WriteFile("untracked.txt", "u\n");

        using var svc = new RepositoryService(t.Path);
        var changes = svc.EnumerateChanges(
            new DiffSide.CommitIsh("HEAD"),
            new DiffSide.WorkingTree());

        // A single file appearing in multiple layers should appear in multiple rows.
        changes.Where(c => c.Path == "a.txt").Should().HaveCount(2);
    }

    [Fact]
    public void EnumerateChanges_RaisesChangeListUpdatedEvent()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        t.InitialCommit("c1");
        t.WriteFile("a.txt", "b\n");

        using var svc = new RepositoryService(t.Path);
        ChangeListUpdatedEventArgs? captured = null;
        svc.ChangeListUpdated += (_, e) => captured = e;

        svc.EnumerateChanges(new DiffSide.CommitIsh("HEAD"), new DiffSide.WorkingTree());

        captured.Should().NotBeNull();
        captured!.Changes.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void CurrentChanges_ReflectsLastEnumeration()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        t.InitialCommit("c1");
        t.WriteFile("a.txt", "b\n");

        using var svc = new RepositoryService(t.Path);
        svc.CurrentChanges.Should().BeEmpty();

        svc.EnumerateChanges(new DiffSide.CommitIsh("HEAD"), new DiffSide.WorkingTree());
        svc.CurrentChanges.Should().NotBeEmpty();
    }

    [Fact]
    public void SnapshotAndSubscribe_ReturnsCurrentSnapshotAndSubscribes()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        t.InitialCommit("c1");
        t.WriteFile("a.txt", "b\n");

        using var svc = new RepositoryService(t.Path);
        svc.EnumerateChanges(new DiffSide.CommitIsh("HEAD"), new DiffSide.WorkingTree());

        int eventCount = 0;
        var (snap, sub) = svc.SnapshotAndSubscribe((_, _) => eventCount++);

        snap.Should().HaveCountGreaterThan(0);

        // Re-enumerate; subscription should fire.
        svc.EnumerateChanges(new DiffSide.CommitIsh("HEAD"), new DiffSide.WorkingTree());
        eventCount.Should().Be(1);

        // After dispose, subscription should detach.
        sub.Dispose();
        svc.EnumerateChanges(new DiffSide.CommitIsh("HEAD"), new DiffSide.WorkingTree());
        eventCount.Should().Be(1);
    }

    [Fact]
    public void ResolveCommitIsh_AcceptsBranchAndSha()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        var c1 = t.InitialCommit("c1");

        using var svc = new RepositoryService(t.Path);

        svc.ResolveCommitIsh("HEAD").Should().Be(c1.Sha);
        svc.ResolveCommitIsh(c1.Sha).Should().Be(c1.Sha);
        svc.ResolveCommitIsh("does-not-exist").Should().BeNull();
    }

    [Fact]
    public void ValidateRevisions_TrueOnlyWhenBothResolve()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        var c1 = t.InitialCommit("c1");

        using var svc = new RepositoryService(t.Path);
        svc.ValidateRevisions("HEAD", c1.Sha).Should().BeTrue();
        svc.ValidateRevisions("HEAD", "deadbeef").Should().BeFalse();
        svc.ValidateRevisions("nope", "HEAD").Should().BeFalse();
    }

    [Fact]
    public void ReadSide_AppliesEncodingDetection_ForUtf8WithBom()
    {
        using var t = new TempRepo();
        var utf8Bom = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i' };
        t.WriteBytes("bom.txt", utf8Bom);
        t.InitialCommit("c1");

        // Modify on disk so the file shows in unstaged.
        t.WriteBytes("bom.txt", new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'o' });

        using var svc = new RepositoryService(t.Path);
        var changes = svc.EnumerateChanges(new DiffSide.CommitIsh("HEAD"), new DiffSide.WorkingTree());

        var change = changes.Single(c => c.Path == "bom.txt" && c.Layer == WorkingTreeLayer.Unstaged);
        var leftContent = svc.ReadSide(change, ChangeSide.Left);

        leftContent.Text.Should().Be("hi");
        leftContent.IsBinary.Should().BeFalse();
        leftContent.IsLfsPointer.Should().BeFalse();
    }

    [Fact]
    public void ReadSide_ReturnsBinaryFlagForNulByteContent()
    {
        using var t = new TempRepo();
        t.WriteBytes("bin.dat", new byte[] { 1, 2, 3, 0, 4, 5 });
        t.InitialCommit("c1");

        // Modify so it shows in unstaged.
        t.WriteBytes("bin.dat", new byte[] { 1, 2, 3, 0, 4, 6 });

        using var svc = new RepositoryService(t.Path);
        var changes = svc.EnumerateChanges(new DiffSide.CommitIsh("HEAD"), new DiffSide.WorkingTree());
        var change = changes.Single(c => c.Path == "bin.dat" && c.Layer == WorkingTreeLayer.Unstaged);

        change.IsBinary.Should().BeTrue();
        var content = svc.ReadSide(change, ChangeSide.Left);
        content.IsBinary.Should().BeTrue();
        content.Text.Should().BeEmpty();  // we don't decode binary
    }

    [Fact]
    public void ReadSide_ReturnsLfsPointerFlag()
    {
        using var t = new TempRepo();
        var pointer = "version https://git-lfs.github.com/spec/v1\noid sha256:" + new string('0', 64) + "\nsize 12345\n";
        t.WriteFile("model.bin", pointer);
        t.InitialCommit("c1");

        t.WriteFile("model.bin", pointer + "\n");

        using var svc = new RepositoryService(t.Path);
        var changes = svc.EnumerateChanges(new DiffSide.CommitIsh("HEAD"), new DiffSide.WorkingTree());
        var change = changes.Single(c => c.Path == "model.bin" && c.Layer == WorkingTreeLayer.Unstaged);

        change.IsLfsPointer.Should().BeTrue();
        var content = svc.ReadSide(change, ChangeSide.Left);
        content.IsLfsPointer.Should().BeTrue();
    }

    [Fact]
    public void ReadSide_ReturnsWorkingTreeBytesForUntracked()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        t.InitialCommit("c1");
        t.WriteFile("new.txt", "I am new\n");

        using var svc = new RepositoryService(t.Path);
        var changes = svc.EnumerateChanges(new DiffSide.CommitIsh("HEAD"), new DiffSide.WorkingTree());
        var change = changes.Single(c => c.Path == "new.txt");

        var left = svc.ReadSide(change, ChangeSide.Left);
        left.Text.Should().BeEmpty();   // untracked has no left side

        var right = svc.ReadSide(change, ChangeSide.Right);
        right.Text.Should().Be("I am new\n");
    }

    [Fact]
    public void TryResolveCurrent_ReturnsNullWhenFileNoLongerInLayer()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        t.InitialCommit("c1");
        t.WriteFile("a.txt", "a CHANGED\n");

        using var svc = new RepositoryService(t.Path);

        // Currently in Unstaged layer.
        var resolved = svc.TryResolveCurrent("a.txt", WorkingTreeLayer.Unstaged);
        resolved.Should().NotBeNull();
        resolved!.Layer.Should().Be(WorkingTreeLayer.Unstaged);

        // Stage it - now it shouldn't be in Unstaged.
        t.Stage("a.txt");
        svc.RefreshIndex();

        var stillUnstaged = svc.TryResolveCurrent("a.txt", WorkingTreeLayer.Unstaged);
        stillUnstaged.Should().BeNull();

        // Should now be in Staged.
        var staged = svc.TryResolveCurrent("a.txt", WorkingTreeLayer.Staged);
        staged.Should().NotBeNull();
        staged!.Layer.Should().Be(WorkingTreeLayer.Staged);
    }

    [Fact]
    public void RefreshIndex_PicksUpExternalStageOperation()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "a\n");
        t.InitialCommit("c1");
        t.WriteFile("a.txt", "a CHANGED\n");

        using var svc = new RepositoryService(t.Path);
        var changes1 = svc.EnumerateChanges(new DiffSide.CommitIsh("HEAD"), new DiffSide.WorkingTree());
        changes1.Should().Contain(c => c.Path == "a.txt" && c.Layer == WorkingTreeLayer.Unstaged);

        // External stage.
        t.Stage("a.txt");
        svc.RefreshIndex();

        var changes2 = svc.EnumerateChanges(new DiffSide.CommitIsh("HEAD"), new DiffSide.WorkingTree());
        changes2.Should().Contain(c => c.Path == "a.txt" && c.Layer == WorkingTreeLayer.Staged);
        changes2.Should().NotContain(c => c.Path == "a.txt" && c.Layer == WorkingTreeLayer.Unstaged);
    }

    [Fact]
    public void EnumerateChanges_UnbornHead_TreatsStagedFilesAsAdded()
    {
        using var t = new TempRepo();
        t.WriteFile("a.txt", "alpha\n");
        t.Stage("a.txt");

        using var svc = new RepositoryService(t.Path);
        svc.Shape.IsHeadUnborn.Should().BeTrue();

        var changes = svc.EnumerateChanges(new DiffSide.CommitIsh("HEAD"), new DiffSide.WorkingTree());

        changes.Should().Contain(c => c.Path == "a.txt" && c.Layer == WorkingTreeLayer.Staged && c.Status == FileStatus.Added);
    }

    [Fact]
    public void EnumerateChanges_DetectsRenamesByDefault()
    {
        using var t = new TempRepo();
        t.WriteFile("old.txt", string.Concat(Enumerable.Repeat("alpha bravo charlie\n", 20)));
        var c1 = t.InitialCommit("c1");

        // Rename via working tree + stage.
        t.DeleteWorkingFile("old.txt");
        t.WriteFile("new.txt", string.Concat(Enumerable.Repeat("alpha bravo charlie\n", 20)));
        // Stage explicit add+remove so libgit2 sees rename similarity.
        using (var repo = new Repository(t.Path))
        {
            Commands.Stage(repo, "*");
        }
        var c2 = t.Commit("rename");

        using var svc = new RepositoryService(t.Path);
        var changes = svc.EnumerateChanges(
            new DiffSide.CommitIsh(c1.Sha),
            new DiffSide.CommitIsh(c2.Sha));

        var rename = changes.SingleOrDefault(c => c.Status == FileStatus.Renamed);
        rename.Should().NotBeNull("LibGit2Sharp default similarity should detect identical-content rename");
        rename!.Path.Should().Be("new.txt");
        rename.OldPath.Should().Be("old.txt");
        rename.IsRenameOrCopy.Should().BeTrue();
    }
}
