using System;
using Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Utilities_Tests
{
    [TestClass]
    public class GitStatusTest
    {
        [TestMethod]
        public void TestMethod1()
        {
            string[] lines = @"On branch u/geevens/test
Your branch is ahead of 'origin/u/geevens/test' by 1 commit.
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
            GitStatus status = GitStatus.ParseLines(lines);
            Assert.AreEqual(@"[ +1 ~1 -2 | +3 ~1 -2 ]", status.ToString());
        }
    }
}
