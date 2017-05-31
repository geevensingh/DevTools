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
            Assert.AreEqual("2 ahead 5 behind", status.RemoteChanges);
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

        [TestMethod]
        public void TestMergeConflict()
        {
            string[] lines = @"## improve-status-parsing...origin/improve-status-parsing [ahead 5]
M  zune/client/xaml/music/UI/Music.UI.vcxproj
UU zune/client/xaml/music/UI/Music.UI.vcxproj.filters
M  zune/client/xaml/music/UI/Services/NowPlayingViewManager.cpp
".Split(new string[] { "\r\n" }, StringSplitOptions.None);
            GitStatus status = GitStatus.ParseLines(lines);
            Assert.AreEqual(@"improve-status-parsing", status.Branch);
            Assert.AreEqual("5 ahead", status.RemoteChanges);
            Assert.AreEqual(@"[ +0 ~2 -0 #1 | +0 ~0 -0 #1 ! ]", status.AllLocalChanges);
            Assert.AreEqual(@"[ +0 ~2 -0 #1 | +0 ~0 -0 #1 ! ]", status.MimimalLocalChanges);
        }

        [TestMethod]
        public void TestRename()
        {
            string[] lines = @"## improve-status-parsing...origin/improve-status-parsing [ahead 5]
 M tools/FilterWriter.exe
R  WECTools/FilterWriter/FilterWriter.sln -> tools/FilterWriter/FilterWriter.sln
?? tools/FilterWriter/App.config
?? tools/FilterWriter/FilterWriter.csproj
?? tools/FilterWriter/Logger.cs
?? tools/FilterWriter/Node.cs
?? tools/FilterWriter/Program.cs
?? tools/FilterWriter/Properties/
".Split(new string[] { "\r\n" }, StringSplitOptions.None);
            GitStatus status = GitStatus.ParseLines(lines);
            Assert.AreEqual(@"improve-status-parsing", status.Branch);
            Assert.AreEqual("5 ahead", status.RemoteChanges);
            Assert.AreEqual(@"[ +0 ~1 -0 | +6 ~1 -0 ! ]", status.AllLocalChanges);
            Assert.AreEqual(@"[ +0 ~1 -0 | +6 ~1 -0 ! ]", status.MimimalLocalChanges);
        }

        [TestMethod]
        public void TestCriticals()
        {
            string[] lines = @"## feature/personalize-playlist...origin/feature/personalize-playlist
UU zune/client/xaml/music/UI/Controls/NowPlayingHeaderControl.xaml.cpp
UU zune/client/xaml/music/UI/Controls/NowPlayingHeaderControl.xaml.h
A  zune/client/xaml/music/Visualization/Visualizations/CameraAngle.h
A  zune/client/xaml/music/Visualization/Visualizations/CameraManagerBase.cpp
A  zune/client/xaml/music/Visualization/Visualizations/CameraManagerBase.hA  zune/client/xaml/music/Visualization/Visualizations/RibbonsVisualization/RibbonsCameraManager.cpp
A  zune/client/xaml/music/Visualization/Visualizations/RibbonsVisualization/RibbonsCameraManager.h
M  zune/client/xaml/music/Visualization/Visualizations/RibbonsVisualization/RibbonsComponent.cpp
M  zune/client/xaml/music/Visualization/Visualizations/RibbonsVisualization/RibbonsComponent.h
C  zune/client/xaml/music/Visualization/Visualizations/RibbonsVisualization/RibbonsComponent.cpp -> zune/client/xaml/music/Visualization/Visualizations/RibbonsVisualization/RibbonsMeshComponent.cpp
C  zune/client/xaml/music/Visualization/Visualizations/RibbonsVisualization/RibbonsComponent.h -> zune/client/xaml/music/Visualization/Visualizations/RibbonsVisualization/RibbonsMeshComponent.h
M  zune/client/xaml/music/Visualization/Visualizations/RibbonsVisualization/RibbonsShaderExtension.cpp
M  zune/client/xaml/music/Visualization/Visualizations/RibbonsVisualization/RibbonsShaderExtension.h
D  zune/client/xaml/music/Visualization/Visualizations/RibbonsVisualization/RibbonsVisualization.cpp
M  zune/client/xaml/music/Visualization/Visualizations/RibbonsVisualization/RibbonsVisualization.hA  zune/client/xaml/shared/Core/Controls/WhatsNewV2Tier2ItemControl.xaml.h
M  zune/client/xaml/shared/Core/Framework/CommandIds.hM  zune/client/xaml/strings/af-ZA/resw/MusicStrings/resources.resw
M  zune/client/xaml/strings/af-ZA/resw/VideoStrings/resources.resw
M  zune/client/xaml/strings/am-ET/resw/CommonStrings/resources.resw
UU zune/client/xaml/strings/am-ET/resw/MusicStrings/resources.reswM  zune/client/xaml/strings/km-KH/resw/VideoStrings/resources.resw
M  zune/client/xaml/strings/kn-IN/resw/CommonStrings/resources.resw
M  zune/client/xaml/strings/kn-IN/resw/MusicStrings/resources.reswUU zune/client/xaml/strings/sv-SE/resw/CommonStrings/resources.resw
UU zune/client/xaml/strings/sv-SE/resw/MusicStrings/resources.resw
M  zune/client/xaml/strings/sv-SE/resw/VideoStrings/resources.resw
UU zune/client/xaml/strings/sv-SE/xml/Android.Xbox.Music.Loc.xmlM  zune/client/xaml/video/ViewModel/PageModels/VideoNowPlayingPageViewModel.h
M  zune/client/xaml/video/ViewModel/PageModels/VideoSettingsPageViewModel.cpp

".Split(new string[] { "\r\n" }, StringSplitOptions.None);
            GitStatus status = GitStatus.ParseLines(lines);
            Assert.AreEqual(@"feature/personalize-playlist", status.Branch);
            Assert.AreEqual(GitStatus.UpToDateString, status.RemoteChanges);
            Assert.AreEqual(@"[ +4 ~14 -1 #5 | +0 ~0 -0 #5 ! ]", status.AllLocalChanges);
            Assert.AreEqual(@"[ +4 ~14 -1 #5 | +0 ~0 -0 #5 ! ]", status.MimimalLocalChanges);
        }
    }
}
