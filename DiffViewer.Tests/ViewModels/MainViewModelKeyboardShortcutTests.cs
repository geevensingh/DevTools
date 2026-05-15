using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

/// <summary>
/// Tests for the keyboard-shortcut commands wired onto <see cref="MainViewModel"/>:
/// cross-file F7/F8 navigation, file-step (Shift+F7/F8), section-step
/// (Ctrl+F7/F8), toggle aliases (Ctrl+W, Ctrl+Shift+W, Ctrl+I, Ctrl+D,
/// Ctrl+L), display-mode setters (Ctrl+1/2/3), refresh (F5), focus-cycle
/// (F6), and zoom commands on <see cref="DiffPaneViewModel"/>.
///
/// The MainViewModel construction goes through a stub
/// <see cref="FakeRepoForKeyboard"/> so we never touch a real filesystem.
/// Tests that exercise hunk/file navigation must run inside a UI
/// synchronization context because <c>DiffPane.LoadAsync</c> uses
/// <c>TaskScheduler.FromCurrentSynchronizationContext()</c>.
/// </summary>
public class MainViewModelKeyboardShortcutTests
{
    // ===================== DiffPane FontSize / Zoom =====================

    [Fact]
    public void DiffPane_FontSize_DefaultIs11()
    {
        var repo = new FakeRepoForKeyboard();
        var vm = new DiffPaneViewModel(repo);
        vm.FontSize.Should().Be(11.0);
    }

    [Fact]
    public void DiffPane_FontSize_ClampedAbove72()
    {
        var repo = new FakeRepoForKeyboard();
        var vm = new DiffPaneViewModel(repo);

        vm.FontSize = 200.0;
        vm.FontSize.Should().Be(DiffPaneViewModel.MaxFontSize);
    }

    [Fact]
    public void DiffPane_FontSize_ClampedBelow6()
    {
        var repo = new FakeRepoForKeyboard();
        var vm = new DiffPaneViewModel(repo);

        vm.FontSize = 1.0;
        vm.FontSize.Should().Be(DiffPaneViewModel.MinFontSize);
    }

    [Fact]
    public void DiffPane_ZoomIn_IncrementsBy1AndPersists()
    {
        var repo = new FakeRepoForKeyboard();
        var settings = new InMemorySettingsServiceK();
        var vm = new DiffPaneViewModel(repo, null, false, settings);

        vm.ZoomInCommand.Execute(null);

        vm.FontSize.Should().Be(12.0);
        settings.Current.FontSize.Should().Be(12.0);
    }

    [Fact]
    public void DiffPane_ZoomOut_DecrementsBy1AndPersists()
    {
        var repo = new FakeRepoForKeyboard();
        var settings = new InMemorySettingsServiceK();
        var vm = new DiffPaneViewModel(repo, null, false, settings);
        vm.FontSize = 14.0;

        vm.ZoomOutCommand.Execute(null);

        vm.FontSize.Should().Be(13.0);
        settings.Current.FontSize.Should().Be(13.0);
    }

    [Fact]
    public void DiffPane_ZoomIn_StopsAt72()
    {
        var repo = new FakeRepoForKeyboard();
        var vm = new DiffPaneViewModel(repo);
        vm.FontSize = 71.5;

        vm.ZoomInCommand.Execute(null);

        vm.FontSize.Should().Be(72.0);
        // One more should be a no-op (already clamped).
        vm.ZoomInCommand.Execute(null);
        vm.FontSize.Should().Be(72.0);
    }

    [Fact]
    public void DiffPane_ZoomOut_StopsAt6()
    {
        var repo = new FakeRepoForKeyboard();
        var vm = new DiffPaneViewModel(repo);
        vm.FontSize = 6.5;

        vm.ZoomOutCommand.Execute(null);

        vm.FontSize.Should().Be(6.0);
        vm.ZoomOutCommand.Execute(null);
        vm.FontSize.Should().Be(6.0);
    }

    [Fact]
    public void DiffPane_ZoomReset_RestoresHardcodedDefault()
    {
        // ZoomReset always returns to the system default 11pt rather than the
        // last-persisted value, because zoom commands themselves persist
        // (per plan); without a hardcoded baseline, "reset" would have no
        // useful target.
        var repo = new FakeRepoForKeyboard();
        var settings = new InMemorySettingsServiceK(new AppSettings { FontSize = 14.0 });
        var vm = new DiffPaneViewModel(repo, null, false, settings);
        vm.FontSize.Should().Be(14.0, "initial seed pulls configured default");

        vm.ZoomInCommand.Execute(null);
        vm.ZoomInCommand.Execute(null);
        vm.FontSize.Should().Be(16.0);

        vm.ZoomResetCommand.Execute(null);
        vm.FontSize.Should().Be(11.0);
    }

    // ===================== Cross-file navigation primitives =====================

    [Fact]
    public async Task TryNavigateNextHunkInFile_ReturnsFalseAtLastHunk()
    {
        // 16-line input with edits at lines 1 and 14 => two hunks
        // (3-line context can't bridge the gap).
        var repo = new FakeRepoForKeyboard
        {
            LeftText  = string.Join("\n", new[]
            {
                "first-old", "b","c","d","e","f","g","h","i","j","k","l","m",
                "last-line", "n", "o",
            }) + "\n",
            RightText = string.Join("\n", new[]
            {
                "first-new", "b","c","d","e","f","g","h","i","j","k","l","m",
                "last-line+", "n", "o",
            }) + "\n",
        };
        var diff = new DiffService();

        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(EntryFor("a.cs"));

            // After load we expect 2 hunks; navigate to the last one,
            // then verify the cross-file primitive returns false.
            vm.JumpToLastHunk();
            // We are now at the last hunk.
            vm.IsAtLastHunk.Should().BeTrue();
            vm.TryNavigateNextHunkInFile().Should().BeFalse();
        });
    }

    [Fact]
    public async Task TryNavigatePreviousHunkInFile_ReturnsFalseAtFirstHunk()
    {
        var repo = new FakeRepoForKeyboard
        {
            LeftText  = string.Join("\n", new[]
            {
                "first-old", "b","c","d","e","f","g","h","i","j","k","l","m",
                "last-line", "n", "o",
            }) + "\n",
            RightText = string.Join("\n", new[]
            {
                "first-new", "b","c","d","e","f","g","h","i","j","k","l","m",
                "last-line+", "n", "o",
            }) + "\n",
        };
        var diff = new DiffService();

        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(EntryFor("a.cs"));

            vm.JumpToFirstHunk();
            vm.IsAtFirstHunk.Should().BeTrue();
            vm.TryNavigatePreviousHunkInFile().Should().BeFalse();
        });
    }

    [Fact]
    public async Task JumpToFirstHunk_LandsOnFirstHunk()
    {
        var repo = new FakeRepoForKeyboard
        {
            LeftText  = "alpha\nbeta\ngamma\n",
            RightText = "alpha\nBETA\ngamma\n",
        };
        var diff = new DiffService();

        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);
            await vm.LoadAsync(EntryFor("a.cs"));

            vm.JumpToFirstHunk();
            vm.IsAtFirstHunk.Should().BeTrue();
        });
    }

    [Fact]
    public async Task LastLoadTask_CompletesAfterLoadAsync()
    {
        var repo = new FakeRepoForKeyboard { LeftText = "a\n", RightText = "b\n" };
        var diff = new DiffService();

        await RunOnUiSyncContextAsync(async () =>
        {
            var vm = new DiffPaneViewModel(repo, diff);

            var loadTask = vm.LoadAsync(EntryFor("a.cs"));
            // LastLoadTask should be the task we just created (or
            // already completed if load finished quickly).
            vm.LastLoadTask.Should().NotBeNull();
            await loadTask;
            vm.LastLoadTask.IsCompleted.Should().BeTrue();
        });
    }

    // ===================== MainViewModel cross-file orchestration =====================

    [Fact]
    public async Task SelectingFile_AutoScrollsCaretToFirstHunk()
    {
        var repo = new FakeRepoForKeyboard();
        repo.SetChanges(ModifiedChange("a.cs"));
        // 1 hunk at line 1 - if auto-scroll fires, CurrentHunkIndex should
        // land on 0; otherwise it stays at the post-load default of -1.
        repo.LeftText  = "alpha\nbeta\n";
        repo.RightText = "ALPHA\nbeta\n";

        await RunOnUiSyncContextAsync(async vm =>
        {
            vm.RefreshChangeList();
            var entry = vm.FileList.FlatEntries[0];

            vm.FileList.SelectedEntry = entry;
            await vm.DiffPane.LastLoadTask;

            vm.DiffPane.CurrentHunkIndex.Should().Be(0,
                "selecting a file should auto-scroll the first hunk into view");
            vm.DiffPane.CurrentHunks.Should().HaveCount(1);
        }, repo);
    }

    [Fact]
    public async Task RefreshChangeList_DoesNotAutoJumpToFirstHunk_WhenSelectedFileUnchanged()
    {
        // Regression guard: a refresh (RepositoryWatcher fire, F5, or
        // post-write-op reload) replaces every FileEntryViewModel even when
        // nothing changed on disk, which fires SelectedEntry PropertyChanged
        // for the currently-loaded file. The handler used to unconditionally
        // call LoadAndScrollToFirstHunkAsync, yanking the user's scroll back
        // to the top of the diff on every refresh. Verify the same-file
        // path now suppresses the auto-jump.
        var repo = new FakeRepoForKeyboard();
        repo.SetChanges(ModifiedChange("a.cs"));
        repo.LeftText  = "alpha\nbeta\ngamma\n";
        repo.RightText = "ALPHA\nbeta\ngamma\n";

        await RunOnUiSyncContextAsync(async vm =>
        {
            vm.RefreshChangeList();
            var entry = vm.FileList.FlatEntries[0];
            vm.FileList.SelectedEntry = entry;
            await vm.DiffPane.LastLoadTask;

            vm.DiffPane.CurrentHunkIndex.Should().Be(0,
                "initial selection auto-scrolls to the first hunk");

            int navCount = 0;
            vm.DiffPane.HunkNavigationRequested += (_, _) => navCount++;

            // Simulate a refresh: rebuilds FileEntryViewModel instances and
            // re-assigns SelectedEntry to a new (but path-equivalent) row.
            vm.RefreshChangeList();
            await vm.DiffPane.LastLoadTask;

            navCount.Should().Be(0,
                "refresh of an unchanged-path file must NOT raise a HunkNavigationRequested " +
                "(which is what physically scrolls the editor to the first diff)");
            vm.DiffPane.CurrentHunkIndex.Should().Be(0,
                "the user's hunk index from before the refresh should be preserved when the " +
                "diff is structurally unchanged - so subsequent F7/F8 navigation continues " +
                "from where they were, not from hunk 0");
        }, repo);
    }

    [Fact]
    public async Task RefreshChangeList_PreservesHunkIndex_SoNextF8ContinuesFromCurrentHunk()
    {
        // Regression guard for the user-reported bug: "if i was on block 5
        // of 12, and there is a refresh, and then i press f8, i end up back
        // on block 1 of 12 instead of 6 of 12 where i'm supposed to be."
        // The fix preserves CurrentHunkIndex across a same-file refresh
        // when the diff is structurally unchanged, so the next F8
        // (TryNavigateNextHunkInFile) advances to index+1 from where the
        // user actually was.
        var repo = new FakeRepoForKeyboard
        {
            // Two-hunk fixture: edits at lines 1 and 14 separated by enough
            // context to defeat hunk merging (matches the pattern used by
            // the F7/F8 cross-file tests in this file).
            LeftText  = string.Join("\n", new[]
            {
                "first-old", "b","c","d","e","f","g","h","i","j","k","l","m",
                "last-line", "n", "o",
            }) + "\n",
            RightText = string.Join("\n", new[]
            {
                "first-new", "b","c","d","e","f","g","h","i","j","k","l","m",
                "last-line+", "n", "o",
            }) + "\n",
        };
        repo.SetChanges(ModifiedChange("a.cs"));

        await RunOnUiSyncContextAsync(async vm =>
        {
            vm.RefreshChangeList();
            var entry = vm.FileList.FlatEntries[0];
            vm.FileList.SelectedEntry = entry;
            await vm.DiffPane.LastLoadTask;

            vm.DiffPane.CurrentHunks.Should().HaveCount(2,
                "fixture should produce a 2-hunk diff");

            // User navigates to hunk #2 of 2 (the analog of "hunk 5 of 12").
            vm.NavigateNextChangeCommand.Execute(null);
            vm.DiffPane.CurrentHunkIndex.Should().Be(1,
                "F8 from hunk 0 advances to hunk 1");

            // Refresh fires (e.g. RepositoryWatcher debounced event).
            vm.RefreshChangeList();
            await vm.DiffPane.LastLoadTask;

            // F8 from the preserved hunk 1 has nowhere to go inside the file
            // (only 2 hunks), so TryNavigateNextHunkInFile returns false.
            // Before the fix, the refresh reset CurrentHunkIndex to -1, so
            // F8 would have stepped to hunk 0 instead of falling through.
            vm.DiffPane.TryNavigateNextHunkInFile().Should().BeFalse(
                "after refresh the caret should still be at the last hunk - if the index " +
                "got reset, this would advance to hunk 0 instead of returning false");
            vm.DiffPane.CurrentHunkIndex.Should().Be(1,
                "the preserved hunk index must survive the refresh unchanged");
        }, repo);
    }

    [Fact]
    public async Task SelectingDifferentFile_StillAutoScrollsToFirstHunk()
    {
        // Guard the inverse of the regression above: switching to a
        // genuinely different file must still auto-scroll to its first hunk.
        var repo = new FakeRepoForKeyboard();
        repo.SetChanges(ModifiedChange("a.cs"), ModifiedChange("b.cs"));
        repo.LeftText  = "x\ny\n";
        repo.RightText = "X\ny\n";

        await RunOnUiSyncContextAsync(async vm =>
        {
            vm.RefreshChangeList();
            var e0 = vm.FileList.FlatEntries[0];
            var e1 = vm.FileList.FlatEntries[1];

            vm.FileList.SelectedEntry = e0;
            await vm.DiffPane.LastLoadTask;

            int navCount = 0;
            vm.DiffPane.HunkNavigationRequested += (_, _) => navCount++;

            vm.FileList.SelectedEntry = e1;
            await vm.DiffPane.LastLoadTask;

            navCount.Should().Be(1,
                "selecting a different file should still raise the auto-jump-to-first-hunk navigation");
            vm.DiffPane.CurrentHunkIndex.Should().Be(0);
        }, repo);
    }

    [Fact]
    public async Task NavigateNextChange_AdvancesAcrossFileBoundary_AndCyclesAtEnd()
    {
        var repo = new FakeRepoForKeyboard();
        // Three modified files in the change list, all in Unstaged.
        repo.SetChanges(
            ModifiedChange("a.cs"),
            ModifiedChange("b.cs"),
            ModifiedChange("c.cs"));
        repo.LeftText  = "x\ny\n";
        repo.RightText = "X\ny\n";   // 1 hunk per file

        await RunOnUiSyncContextAsync(async vm =>
        {
            vm.RefreshChangeList();
            vm.FileList.FlatEntries.Count.Should().Be(3);
            var e0 = vm.FileList.FlatEntries[0];
            var e1 = vm.FileList.FlatEntries[1];
            var e2 = vm.FileList.FlatEntries[2];

            vm.FileList.SelectedEntry = e0;
            await vm.DiffPane.LastLoadTask;

            // Each file has exactly 1 hunk. Auto-scroll on file selection
            // already lands the caret on hunk #0 of e0, so the first F8
            // immediately steps to the next file.
            // F8 #1: idx=0 is last hunk of e0 → step to e1.
            // F8 #2: idx=0 is last hunk of e1 → step to e2.
            // F8 #3: step from e2 → cycle to e0.
            await vm.NavigateNextChangeCommand.ExecuteAsync(null);
            vm.FileList.SelectedEntry.Should().Be(e1,
                $"first F8 should advance to next file (hunk index was {vm.DiffPane.CurrentHunkIndex})");

            await vm.NavigateNextChangeCommand.ExecuteAsync(null);
            vm.FileList.SelectedEntry.Should().Be(e2);

            await vm.NavigateNextChangeCommand.ExecuteAsync(null);
            vm.FileList.SelectedEntry.Should().Be(e0, "should cycle back to first file");
        }, repo);
    }

    [Fact]
    public async Task NavigateNextFile_SkipsHunkWalkAndCycles()
    {
        var repo = new FakeRepoForKeyboard();
        repo.SetChanges(
            ModifiedChange("a.cs"),
            ModifiedChange("b.cs"));
        repo.LeftText  = "x\n";
        repo.RightText = "y\n";

        await RunOnUiSyncContextAsync(async vm =>
        {
            vm.RefreshChangeList();
            var e0 = vm.FileList.FlatEntries[0];
            var e1 = vm.FileList.FlatEntries[1];
            vm.FileList.SelectedEntry = e0;
            await vm.DiffPane.LastLoadTask;

            await vm.NavigateNextFileCommand.ExecuteAsync(null);
            vm.FileList.SelectedEntry.Should().Be(e1);

            await vm.NavigateNextFileCommand.ExecuteAsync(null);
            vm.FileList.SelectedEntry.Should().Be(e0,
                "should cycle around to the first entry");
        }, repo);
    }

    [Fact]
    public async Task NavigatePreviousFile_StepsBackwardAndCycles()
    {
        var repo = new FakeRepoForKeyboard();
        repo.SetChanges(ModifiedChange("a.cs"), ModifiedChange("b.cs"));
        repo.LeftText  = "x\n";
        repo.RightText = "y\n";

        await RunOnUiSyncContextAsync(async vm =>
        {
            vm.RefreshChangeList();
            var e0 = vm.FileList.FlatEntries[0];
            var e1 = vm.FileList.FlatEntries[1];
            vm.FileList.SelectedEntry = e1;
            await vm.DiffPane.LastLoadTask;

            await vm.NavigatePreviousFileCommand.ExecuteAsync(null);
            vm.FileList.SelectedEntry.Should().Be(e0);

            await vm.NavigatePreviousFileCommand.ExecuteAsync(null);
            vm.FileList.SelectedEntry.Should().Be(e1,
                "should cycle around to the last entry");
        }, repo);
    }

    [Fact]
    public async Task NavigatePreviousChange_FromFirstHunk_StepsToPreviousFile_AndLandsOnLastHunk()
    {
        // Two files, each with 2 hunks. From the first hunk of the second
        // file, F7 has nowhere to go within the file and falls through to
        // StepFile(forward:false, jumpToFirst:false). The post-load
        // JumpToLastHunk must override the auto-scroll-to-first that
        // OnFileListPropertyChanged kicks off via LoadAndScrollToFirstHunkAsync,
        // landing the caret on the LAST hunk of the previous file.
        // Regression guard for the continuation-ordering issue described
        // in https://github.com/geevensingh/DevTools/issues/25.
        var repo = new FakeRepoForKeyboard
        {
            // 16-line input with edits at lines 1 and 14 => two hunks
            // (3-line context can't bridge the gap). Same pattern used
            // by TryNavigateNextHunkInFile_ReturnsFalseAtLastHunk above.
            LeftText  = string.Join("\n", new[]
            {
                "first-old", "b","c","d","e","f","g","h","i","j","k","l","m",
                "last-line", "n", "o",
            }) + "\n",
            RightText = string.Join("\n", new[]
            {
                "first-new", "b","c","d","e","f","g","h","i","j","k","l","m",
                "last-line+", "n", "o",
            }) + "\n",
        };
        repo.SetChanges(ModifiedChange("a.cs"), ModifiedChange("b.cs"));

        await RunOnUiSyncContextAsync(async vm =>
        {
            vm.RefreshChangeList();
            var e0 = vm.FileList.FlatEntries[0];
            var e1 = vm.FileList.FlatEntries[1];

            vm.FileList.SelectedEntry = e1;
            await vm.DiffPane.LastLoadTask;

            // Auto-scroll-on-select landed the caret on hunk 0 of e1.
            // Both files share the same content via the fake repo, so
            // each is expected to have 2 hunks.
            vm.DiffPane.CurrentHunks.Should().HaveCount(2,
                "fixture should produce a 2-hunk diff per file");
            vm.DiffPane.CurrentHunkIndex.Should().Be(0,
                "auto-scroll should have landed on the first hunk");

            await vm.NavigatePreviousChangeCommand.ExecuteAsync(null);

            vm.FileList.SelectedEntry.Should().Be(e0,
                "F7 from the first hunk should step back to the previous file");
            vm.DiffPane.CurrentHunkIndex.Should().Be(
                vm.DiffPane.CurrentHunks.Count - 1,
                "F7 across files must land on the LAST hunk of the previous file " +
                "(JumpToLastHunk in StepFile must overwrite the auto-scroll's " +
                "JumpToFirstHunk that LoadAndScrollToFirstHunkAsync chained into " +
                "LastLoadTask)");
        }, repo);
    }

    [Fact]
    public async Task NavigatePreviousFile_LandsOnFirstHunkOfPreviousFile()
    {
        // Symmetric mirror of the above for Shift+F7 (NavigatePreviousFileCommand),
        // which routes through StepFile(forward:false, jumpToFirst:true).
        // With jumpToFirst:true, the explicit post-load JumpToFirstHunk
        // is a no-op overlay on the auto-scroll's same JumpToFirstHunk -
        // both should land on hunk 0 (not hunk Count-1), proving the
        // jumpToFirst arg is honoured even though backward stepping
        // would naively suggest the last hunk.
        var repo = new FakeRepoForKeyboard
        {
            LeftText  = string.Join("\n", new[]
            {
                "first-old", "b","c","d","e","f","g","h","i","j","k","l","m",
                "last-line", "n", "o",
            }) + "\n",
            RightText = string.Join("\n", new[]
            {
                "first-new", "b","c","d","e","f","g","h","i","j","k","l","m",
                "last-line+", "n", "o",
            }) + "\n",
        };
        repo.SetChanges(ModifiedChange("a.cs"), ModifiedChange("b.cs"));

        await RunOnUiSyncContextAsync(async vm =>
        {
            vm.RefreshChangeList();
            var e0 = vm.FileList.FlatEntries[0];
            var e1 = vm.FileList.FlatEntries[1];

            vm.FileList.SelectedEntry = e1;
            await vm.DiffPane.LastLoadTask;
            vm.DiffPane.CurrentHunks.Should().HaveCount(2);

            await vm.NavigatePreviousFileCommand.ExecuteAsync(null);

            vm.FileList.SelectedEntry.Should().Be(e0,
                "Shift+F7 should step backward to the previous file");
            vm.DiffPane.CurrentHunkIndex.Should().Be(0,
                "Shift+F7 routes through StepFile(forward:false, jumpToFirst:true) " +
                "so the caret must land on the FIRST hunk of the previous file, " +
                "even though the navigation direction is backward");
        }, repo);
    }

    [Fact]
    public async Task NavigateNextChange_SkipsWhitespaceOnlyFiles()
    {
        var repo = new FakeRepoForKeyboard();
        repo.SetChanges(
            ModifiedChange("a.cs"),
            ModifiedChange("b.cs"));
        repo.LeftText  = "x\n";
        repo.RightText = "y\n";

        await RunOnUiSyncContextAsync(async vm =>
        {
            vm.RefreshChangeList();
            var e0 = vm.FileList.FlatEntries[0];
            var e1 = vm.FileList.FlatEntries[1];
            // Mark entry 1 as confirmed-no-changes (whitespace-only).
            e1.HasVisibleDifferences = false;
            vm.FileList.SelectedEntry = e0;
            await vm.DiffPane.LastLoadTask;

            // F8 #1: auto-scroll already landed caret at idx=0 of e0 (last
            // hunk), so F8 walks past e1 (whitespace-only) and cycles to e0.
            await vm.NavigateNextChangeCommand.ExecuteAsync(null);
            vm.FileList.SelectedEntry.Should().Be(e0);

            // F8 #2: same as above — single-hunk e0 cycles back to itself
            // because e1 has no visible differences to land on.
            await vm.NavigateNextChangeCommand.ExecuteAsync(null);
            vm.FileList.SelectedEntry.Should().Be(e0,
                "should skip whitespace-only e1 and cycle back");
        }, repo);
    }

    // ===================== Toggle commands =====================

    [Fact]
    public void ToggleIgnoreWhitespace_FlipsDiffPaneState()
    {
        using var fixture = new KeyboardFixture();
        var initial = fixture.Vm.DiffPane.IgnoreWhitespace;
        fixture.Vm.ToggleIgnoreWhitespaceCommand.Execute(null);
        fixture.Vm.DiffPane.IgnoreWhitespace.Should().Be(!initial);
    }

    [Fact]
    public void ToggleVisibleWhitespace_FlipsDiffPaneState()
    {
        using var fixture = new KeyboardFixture();
        var initial = fixture.Vm.DiffPane.ShowVisibleWhitespace;
        fixture.Vm.ToggleVisibleWhitespaceCommand.Execute(null);
        fixture.Vm.DiffPane.ShowVisibleWhitespace.Should().Be(!initial);
    }

    [Fact]
    public void ToggleSideBySide_FlipsDiffPaneState()
    {
        using var fixture = new KeyboardFixture();
        var initial = fixture.Vm.DiffPane.IsSideBySide;
        fixture.Vm.ToggleSideBySideCommand.Execute(null);
        fixture.Vm.DiffPane.IsSideBySide.Should().Be(!initial);
    }

    [Fact]
    public void ToggleIntraLineDiff_FlipsDiffPaneState()
    {
        using var fixture = new KeyboardFixture();
        var initial = fixture.Vm.DiffPane.ShowIntraLineDiff;
        fixture.Vm.ToggleIntraLineDiffCommand.Execute(null);
        fixture.Vm.DiffPane.ShowIntraLineDiff.Should().Be(!initial);
    }

    [Fact]
    public void ToggleLiveUpdates_NoOpWhenCommitVsCommit()
    {
        using var fixture = new KeyboardFixture(commitVsCommit: true);
        var initial = fixture.Vm.DiffPane.LiveUpdates;

        fixture.Vm.ToggleLiveUpdatesCommand.Execute(null);

        // Greyed-out path: state unchanged.
        fixture.Vm.DiffPane.LiveUpdates.Should().Be(initial);
    }

    [Fact]
    public void ToggleLiveUpdates_FlipsWhenWorkingTreeMode()
    {
        using var fixture = new KeyboardFixture(commitVsCommit: false);
        var initial = fixture.Vm.DiffPane.LiveUpdates;
        fixture.Vm.ToggleLiveUpdatesCommand.Execute(null);
        fixture.Vm.DiffPane.LiveUpdates.Should().Be(!initial);
    }

    // ===================== Display-mode commands =====================

    [Fact]
    public void SetDisplayMode_FullPath_RepoRelative_Grouped_AllFlipFileList()
    {
        using var fixture = new KeyboardFixture();

        fixture.Vm.SetDisplayModeFullPathCommand.Execute(null);
        fixture.Vm.FileList.DisplayMode.Should().Be(FileListDisplayMode.FullPath);

        fixture.Vm.SetDisplayModeRepoRelativeCommand.Execute(null);
        fixture.Vm.FileList.DisplayMode.Should().Be(FileListDisplayMode.RepoRelative);

        fixture.Vm.SetDisplayModeGroupedCommand.Execute(null);
        fixture.Vm.FileList.DisplayMode.Should().Be(FileListDisplayMode.GroupedByDirectory);
    }

    // ===================== Refresh + focus-cycle =====================

    [Fact]
    public void Refresh_TriggersChangeListReload_AndToasts()
    {
        using var fixture = new KeyboardFixture();
        fixture.Repo.SetChanges(ModifiedChange("z.cs"));
        var toasts = new List<string>();
        fixture.Vm.ToastHandler = toasts.Add;

        fixture.Vm.RefreshCommand.Execute(null);

        fixture.Vm.FileList.FlatEntries.Should().ContainSingle();
        toasts.Should().ContainSingle().Which.Should().StartWith("Refreshed");
    }

    [Fact]
    public void Refresh_NoChanges_ToastsNoChanges()
    {
        using var fixture = new KeyboardFixture();
        var toasts = new List<string>();
        fixture.Vm.ToastHandler = toasts.Add;

        fixture.Vm.RefreshCommand.Execute(null);

        toasts.Should().ContainSingle().Which.Should().Contain("No changes");
    }

    [Fact]
    public void CycleFocus_InvokesFocusCycleRequestedHook()
    {
        using var fixture = new KeyboardFixture();
        int hookFired = 0;
        fixture.Vm.FocusCycleRequested = () => hookFired++;

        fixture.Vm.CycleFocusCommand.Execute(null);

        hookFired.Should().Be(1);
    }

    [Fact]
    public void WindowTitle_FormatsWorkingTreeAsHumanFriendlyString()
    {
        using var fixture = new KeyboardFixture();

        // Default fixture is working tree -> HEAD.
        fixture.Vm.WindowTitle.Should().Contain("working tree");
        fixture.Vm.WindowTitle.Should().Contain("HEAD");
        fixture.Vm.WindowTitle.Should().NotContain("<working-tree>",
            "the angle-bracketed sentinel from DiffSide.WorkingTree.ToString() should not appear in the title bar");
    }

    [Fact]
    public void WindowTitle_FormatsCommitVsCommit_WithBothRefs()
    {
        using var fixture = new KeyboardFixture(commitVsCommit: true);

        fixture.Vm.WindowTitle.Should().Contain("HEAD~1");
        fixture.Vm.WindowTitle.Should().Contain("HEAD");
    }

    // ===================== Helpers =====================

    private sealed class KeyboardFixture : IDisposable
    {
        public string RepoRoot { get; }
        public FakeRepoForKeyboard Repo { get; }
        public FakeGitWriteServiceK Git { get; }
        public InMemorySettingsServiceK Settings { get; }
        public MainViewModel Vm { get; }

        public KeyboardFixture(bool commitVsCommit = false)
        {
            RepoRoot = Path.Combine(Path.GetTempPath(), "DiffViewerKbd_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RepoRoot);
            Repo = new FakeRepoForKeyboard { Shape_ = MakeShape(RepoRoot) };
            Git = new FakeGitWriteServiceK();
            Settings = new InMemorySettingsServiceK();

            Vm = new MainViewModel(
                repository: Repo,
                left: commitVsCommit ? new DiffSide.CommitIsh("HEAD~1") : new DiffSide.WorkingTree(),
                right: new DiffSide.CommitIsh("HEAD"),
                settingsService: Settings,
                gitWriteService: Git);
        }

        public void Dispose()
        {
            Vm.Dispose();
            try { Directory.Delete(RepoRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    private static RepositoryShape MakeShape(string root) =>
        new(RepoRoot: root,
            WorkingDirectory: root,
            GitDir: Path.Combine(root, ".git"),
            IsBare: false, IsHeadUnborn: false,
            IsSparseCheckout: false, IsPartialClone: false,
            HasInProgressOperation: false);

    private static FileChange ModifiedChange(string path) =>
        new(Path: path, OldPath: null,
            Status: FileStatus.Modified, ConflictCode: null,
            Layer: WorkingTreeLayer.Unstaged,
            LeftBlobSha: "aaaaaaa", RightBlobSha: "bbbbbbb",
            IsBinary: false,
            LeftFileSizeBytes: null, RightFileSizeBytes: null,
            IsLfsPointer: false, IsSparseNotCheckedOut: false,
            OldMode: 33188, NewMode: 33188);

    private static FileEntryViewModel EntryFor(string path) =>
        new(ModifiedChange(path), @"C:\repo");

    private static async Task RunOnUiSyncContextAsync(Func<Task> body)
    {
        var tcs = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try { await body(); tcs.SetResult(); }
                catch (Exception ex) { tcs.SetException(ex); }
                finally { dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await tcs.Task;
    }

    private static async Task RunOnUiSyncContextAsync(
        Func<MainViewModel, Task> body,
        FakeRepoForKeyboard repo)
    {
        var tcs = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                MainViewModel? vm = null;
                try
                {
                    vm = new MainViewModel(
                        repository: repo,
                        left: new DiffSide.WorkingTree(),
                        right: new DiffSide.CommitIsh("HEAD"),
                        diffService: new DiffService(),
                        settingsService: new InMemorySettingsServiceK(),
                        gitWriteService: new FakeGitWriteServiceK());
                    await body(vm);
                    tcs.SetResult();
                }
                catch (Exception ex) { tcs.SetException(ex); }
                finally
                {
                    vm?.Dispose();
                    dispatcher.InvokeShutdown();
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await tcs.Task;
    }

    // ------------- Test fakes (suffixed K to avoid clashing with the
    // identically-purposed types in MainViewModelContextMenuTests) -------------

    private sealed class FakeRepoForKeyboard : IRepositoryService
    {
        public RepositoryShape Shape_ { get; set; } = new(@"C:\repo", @"C:\repo", @"C:\repo\.git", false, false, false, false, false);
        public RepositoryShape Shape => Shape_;

#pragma warning disable CS0067
        public event EventHandler<ChangeListUpdatedEventArgs>? ChangeListUpdated;
        public event EventHandler<RepositoryLostEventArgs>? RepositoryLost;
#pragma warning restore CS0067

        private List<FileChange> _changes = new();
        public IReadOnlyList<FileChange> CurrentChanges => _changes;
        public void SetChanges(params FileChange[] cs) => _changes = cs.ToList();

        public string LeftText { get; set; } = string.Empty;
        public string RightText { get; set; } = string.Empty;

        public string? ResolveCommitIsh(string reference) => "0".PadLeft(40, '0');
        public bool ValidateRevisions(string leftRef, string rightRef) => true;

        public IReadOnlyList<FileChange> EnumerateChanges(DiffSide left, DiffSide right) => _changes;

        public BlobContent ReadSide(FileChange change, ChangeSide side)
        {
            var text = side == ChangeSide.Left ? LeftText : RightText;
            return new BlobContent(
                System.Text.Encoding.UTF8.GetBytes(text),
                System.Text.Encoding.UTF8,
                text,
                IsBinary: false,
                IsLfsPointer: false);
        }

        public void RefreshIndex() { }
        public FileChange? TryResolveCurrent(string path, WorkingTreeLayer layer) => null;
        public bool TryReopen() => true;

        public (IReadOnlyList<FileChange> Snapshot, IDisposable Subscription) SnapshotAndSubscribe(
            EventHandler<ChangeListUpdatedEventArgs> handler)
        {
            ChangeListUpdated += handler;
            return (_changes, new Sub(this, handler));
        }

        public bool IsPathIgnored(string repoRelativeForwardSlashPath) => false;
        public void Dispose() { }

        private sealed class Sub : IDisposable
        {
            private readonly FakeRepoForKeyboard _o;
            private readonly EventHandler<ChangeListUpdatedEventArgs> _h;
            public Sub(FakeRepoForKeyboard o, EventHandler<ChangeListUpdatedEventArgs> h) { _o = o; _h = h; }
            public void Dispose() => _o.ChangeListUpdated -= _h;
        }
    }

    private sealed class FakeGitWriteServiceK : IGitWriteService
    {
#pragma warning disable CS0067
        public event EventHandler<GitWriteOperationEventArgs>? BeforeOperation;
        public event EventHandler<GitWriteOperationEventArgs>? AfterOperation;
#pragma warning restore CS0067
        public Task<GitWriteResult> StageHunkAsync(string r, HunkPatchInputs i, CancellationToken ct = default) => Task.FromResult(GitWriteResult.Ok());
        public Task<GitWriteResult> UnstageHunkAsync(string r, HunkPatchInputs i, CancellationToken ct = default) => Task.FromResult(GitWriteResult.Ok());
        public Task<GitWriteResult> RevertHunkAsync(string r, HunkPatchInputs i, CancellationToken ct = default) => Task.FromResult(GitWriteResult.Ok());
        public Task<GitWriteResult> StageFileAsync(string r, string p, CancellationToken ct = default) => Task.FromResult(GitWriteResult.Ok());
        public Task<GitWriteResult> UnstageFileAsync(string r, string p, CancellationToken ct = default) => Task.FromResult(GitWriteResult.Ok());
        public Task<GitWriteResult> AddToGitignoreAsync(string r, string p, CancellationToken ct = default) => Task.FromResult(GitWriteResult.Ok());
        public Task<GitWriteResult> DeleteToRecycleBinAsync(string r, string p, CancellationToken ct = default) => Task.FromResult(GitWriteResult.Ok());
    }

    private sealed class InMemorySettingsServiceK : ISettingsService
    {
        private AppSettings _current;
        public InMemorySettingsServiceK() : this(new AppSettings()) { }
        public InMemorySettingsServiceK(AppSettings initial) => _current = initial;
        public AppSettings Current => _current;
        public SettingsLoadOutcome LastLoadOutcome => SettingsLoadOutcome.Loaded;
        public event EventHandler<SettingsChangedEventArgs>? Changed;
        public void Save(AppSettings updated)
        {
            var prev = _current;
            _current = updated;
            Changed?.Invoke(this, new SettingsChangedEventArgs(prev, _current));
        }
        public AppSettings Update(Func<AppSettings, AppSettings> mutate)
        {
            Save(mutate(_current));
            return _current;
        }
    }
}
