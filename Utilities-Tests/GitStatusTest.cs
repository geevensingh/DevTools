using System;
using Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Utilities_Tests
{
    [TestClass]
    public class GitStatusTest
    {
        [TestMethod]
        public void TestFileCounts()
        {
            string[] lines = @"On branch u/geevens/test
Your branch is up-to-date with 'origin/u/geevens/test'
  (use ""git push"" to publish your local commits)
Changes to be committed:
  (use ""git reset HEAD <file>..."" to unstage)

        deleted:    AccessibleGridView.cpp
        deleted:    AccessibleGridView.h
        modified:   AppChrome.xaml.cpp
        new file:   foobar.cpp

Changes not staged for commit:
  (use ""git add/rm <file>..."" to update what will be committed)
  (use ""git checkout -- <file>..."" to discard changes in working directory)

        modified:   AppResetDialog.xaml
        deleted:    AutoHidingControl.cpp
        deleted:    XYFocusForwarder.cpp

Untracked files:
  (use ""git add <file>..."" to include in what will be committed)

        blahblah.cpp
        status-b.txt
        status.txt

no changes added to commit (use ""git add"" and/or ""git commit -a"")
".Split(new string[] { "\r\n" }, StringSplitOptions.None);
            lines = @"## improve-status-parsing...origin/improve-status-parsing
M  first
MM second
 M third
 M fourth
 M fifth
 M sixth
D  seventh
D  eighth
D  nineth
 D tenth
 D eleventh
 D twelveth
 D thirteenth
 D fourteenth
 D fifteenth
A  sixteenth
 A seventeenth
 A eighteenth
 A nineteenth
 A twentith
".Split(new string[] { "\r\n" }, StringSplitOptions.None);

            GitStatus status = GitStatus.ParseLines(lines);
            Assert.AreEqual(@"u/geevens/test", status.Branch);
            Assert.AreEqual(GitStatus.UpToDateString, status.RemoteChanges);
            Assert.AreEqual(@"[ +1 ~2 -3 | +4 ~5 -6 ]", status.LocalChanges);
        }

        [TestMethod]
        public void TestBranchAhead()
        {
            string[] lines = @"On branch u/geevens/test
Your branch is ahead of 'origin/u/geevens/test' by 1 commit.
  (use ""git push"" to publish your local commits)
nothing to commit, working tree clean
".Split(new string[] { "\r\n" }, StringSplitOptions.None);
            lines = @"## u/geevens/test...origin/u/geevens/test [ahead 1]
".Split(new string[] { "\r\n" }, StringSplitOptions.None);
            GitStatus status = GitStatus.ParseLines(lines);
            Assert.AreEqual(@"u/geevens/test", status.Branch);
            Assert.AreEqual("1" + GitStatus.AheadString, status.RemoteChanges);
            Assert.AreEqual(@"[ +0 ~0 -0 | +0 ~0 -0 ]", status.LocalChanges);
        }

        [TestMethod]
        public void TestBranchBehind()
        {
            string[] lines = @"On branch u/geevens/test
Your branch is behind 'origin/u/geevens/test' by 32 commits, and can be fast-forwarded.
  (use ""git pull"" to update your local branch)
nothing to commit, working tree clean
".Split(new string[] { "\r\n" }, StringSplitOptions.None);
            lines = @"## u/geevens/test...origin/u/geevens/test [behind 32]
".Split(new string[] { "\r\n" }, StringSplitOptions.None);
            GitStatus status = GitStatus.ParseLines(lines);
            Assert.AreEqual(@"u/geevens/test", status.Branch);
            Assert.AreEqual("32" + GitStatus.BehindString, status.RemoteChanges);
            Assert.AreEqual(@"[ +0 ~0 -0 | +0 ~0 -0 ]", status.LocalChanges);
        }

        [TestMethod]
        public void TestBranchAheadAndBehind()
        {
            string[] lines = @"## u/geevens/test...origin/u/geevens/test [ahead 2, behind 5]
".Split(new string[] { "\r\n" }, StringSplitOptions.None);
            GitStatus status = GitStatus.ParseLines(lines);
            Assert.AreEqual(@"u/geevens/test", status.Branch);
            Assert.AreEqual("2" + GitStatus.AheadString + " 5" + GitStatus.BehindString, status.RemoteChanges);
            Assert.AreEqual(@"[ +0 ~0 -0 | +0 ~0 -0 ]", status.LocalChanges);
        }

        [TestMethod]
        public void TestNoRemote()
        {
            string[] lines = @"## improve-status-parsing
".Split(new string[] { "\r\n" }, StringSplitOptions.None);
            lines = @"On branch improve-status-parsing
nothing to commit, working tree clean
".Split(new string[] { "\r\n" }, StringSplitOptions.None);
            GitStatus status = GitStatus.ParseLines(lines);
            Assert.AreEqual(@"improve-status-parsing", status.Branch);
            Assert.AreEqual(string.Empty, status.RemoteChanges);
            Assert.AreEqual(@"[ +0 ~0 -0 | +0 ~0 -0 ]", status.LocalChanges);
        }

        [TestMethod]
        public void TestCommonCase()
        {
            string[] lines = @"## improve-status-parsing...origin/improve-status-parsing
".Split(new string[] { "\r\n" }, StringSplitOptions.None);
            lines = @"On branch improve-status-parsing
Your branch is up-to-date with 'origin/improve-status-parsing'.
nothing to commit, working tree clean
".Split(new string[] { "\r\n" }, StringSplitOptions.None);
            GitStatus status = GitStatus.ParseLines(lines);
            Assert.AreEqual(@"improve-status-parsing", status.Branch);
            Assert.AreEqual(GitStatus.UpToDateString, status.RemoteChanges);
            Assert.AreEqual(@"[ +0 ~0 -0 | +0 ~0 -0 ]", status.LocalChanges);
        }
    }
}
