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
            string[] lines = @"## u/geevens/test...origin/u/geevens/test
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
            Assert.AreEqual(@"[ +1 ~2 -3 | +4 ~5 -6 ! ]", status.AllLocalChanges);
            Assert.AreEqual(@"[ +1 ~2 -3 | +4 ~5 -6 ! ]", status.MimimalLocalChanges);
        }

        [TestMethod]
        public void TestFileCounts_StagedOnly()
        {
            string[] lines = @"## u/geevens/test...origin/u/geevens/test
M  first
M  second
D  seventh
D  eighth
D  nineth
A  sixteenth
".Split(new string[] { "\r\n" }, StringSplitOptions.None);

            GitStatus status = GitStatus.ParseLines(lines);
            Assert.AreEqual(@"u/geevens/test", status.Branch);
            Assert.AreEqual(GitStatus.UpToDateString, status.RemoteChanges);
            Assert.AreEqual(@"[ +1 ~2 -3 | +0 ~0 -0 ! ]", status.AllLocalChanges);
            Assert.AreEqual(@"[ +1 ~2 -3 ]", status.MimimalLocalChanges);
        }

        [TestMethod]
        public void TestFileCounts_UnstagedOnly()
        {
            string[] lines = @"## u/geevens/test...origin/u/geevens/test
 M second
 M third
 M fourth
 M fifth
 M sixth
 D tenth
 D eleventh
 D twelveth
 D thirteenth
 D fourteenth
 D fifteenth
 A seventeenth
 A eighteenth
 A nineteenth
 A twentith
".Split(new string[] { "\r\n" }, StringSplitOptions.None);

            GitStatus status = GitStatus.ParseLines(lines);
            Assert.AreEqual(@"u/geevens/test", status.Branch);
            Assert.AreEqual(GitStatus.UpToDateString, status.RemoteChanges);
            Assert.AreEqual(@"[ +0 ~0 -0 | +4 ~5 -6 ! ]", status.AllLocalChanges);
            Assert.AreEqual(@"[ +4 ~5 -6 ! ]", status.MimimalLocalChanges);
        }

        [TestMethod]
        public void TestBranchAhead()
        {
            string[] lines = @"## u/geevens/test...origin/u/geevens/test [ahead 1]
".Split(new string[] { "\r\n" }, StringSplitOptions.None);
            GitStatus status = GitStatus.ParseLines(lines);
            Assert.AreEqual(@"u/geevens/test", status.Branch);
            Assert.AreEqual("1 ahead", status.RemoteChanges);
            Assert.AreEqual(@"[ +0 ~0 -0 | +0 ~0 -0 ! ]", status.AllLocalChanges);
            Assert.AreEqual(@"", status.MimimalLocalChanges);
        }

        [TestMethod]
        public void TestBranchBehind()
        {
            string[] lines = @"## u/geevens/test...origin/u/geevens/test [behind 32]
".Split(new string[] { "\r\n" }, StringSplitOptions.None);
            GitStatus status = GitStatus.ParseLines(lines);
            Assert.AreEqual(@"u/geevens/test", status.Branch);
            Assert.AreEqual("32 behind", status.RemoteChanges);
            Assert.AreEqual(@"[ +0 ~0 -0 | +0 ~0 -0 ! ]", status.AllLocalChanges);
            Assert.AreEqual(@"", status.MimimalLocalChanges);
        }

        [TestMethod]
        public void TestBranchAheadAndBehind()
        {
            string[] lines = @"## u/geevens/test...origin/u/geevens/test [ahead 2, behind 5]
".Split(new string[] { "\r\n" }, StringSplitOptions.None);
            GitStatus status = GitStatus.ParseLines(lines);
            Assert.AreEqual(@"u/geevens/test", status.Branch);
            Assert.AreEqual("2 ahead" + " 5 behind", status.RemoteChanges);
            Assert.AreEqual(@"[ +0 ~0 -0 | +0 ~0 -0 ! ]", status.AllLocalChanges);
            Assert.AreEqual(@"", status.MimimalLocalChanges);
        }

        [TestMethod]
        public void TestNoRemote()
        {
            string[] lines = @"## improve-status-parsing
".Split(new string[] { "\r\n" }, StringSplitOptions.None);
            GitStatus status = GitStatus.ParseLines(lines);
            Assert.AreEqual(@"improve-status-parsing", status.Branch);
            Assert.AreEqual("no-remote", status.RemoteChanges);
            Assert.AreEqual(@"[ +0 ~0 -0 | +0 ~0 -0 ! ]", status.AllLocalChanges);
            Assert.AreEqual(@"", status.MimimalLocalChanges);
        }

        [TestMethod]
        public void TestCommonCase()
        {
            string[] lines = @"## improve-status-parsing...origin/improve-status-parsing
".Split(new string[] { "\r\n" }, StringSplitOptions.None);
            GitStatus status = GitStatus.ParseLines(lines);
            Assert.AreEqual(@"improve-status-parsing", status.Branch);
            Assert.AreEqual(GitStatus.UpToDateString, status.RemoteChanges);
            Assert.AreEqual(@"[ +0 ~0 -0 | +0 ~0 -0 ! ]", status.AllLocalChanges);
            Assert.AreEqual(@"", status.MimimalLocalChanges);
        }

        [TestMethod]
        public void TestDeletedRemote()
        {
            string[] lines = @"## improve-status-parsing...origin/improve-status-parsing [gone]
".Split(new string[] { "\r\n" }, StringSplitOptions.None);
            GitStatus status = GitStatus.ParseLines(lines);
            Assert.AreEqual(@"improve-status-parsing", status.Branch);
            Assert.AreEqual("remote-gone", status.RemoteChanges);
            Assert.AreEqual(@"[ +0 ~0 -0 | +0 ~0 -0 ! ]", status.AllLocalChanges);
            Assert.AreEqual(@"", status.MimimalLocalChanges);
        }
    }
}
