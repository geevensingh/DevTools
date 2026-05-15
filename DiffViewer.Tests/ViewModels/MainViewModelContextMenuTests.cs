using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using FluentAssertions;
using System.IO;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

/// <summary>
/// Unit tests for the file-list and diff-pane context-menu commands wired
/// onto <see cref="MainViewModel"/>. Construction goes through a stub
/// <see cref="FakeRepositoryService"/> so we never touch the real file
/// system or LibGit2Sharp; the git-write and external-app launchers are
/// fakes that record call shapes rather than performing side effects.
/// </summary>
public class MainViewModelContextMenuTests : IDisposable
{
    private readonly string _repoRoot;
    private readonly FakeRepositoryService _repo;
    private readonly FakeGitWriteService _git;
    private readonly FakeExternalAppLauncher _launcher;
    private readonly InMemorySettingsService _settings;
    private readonly MainViewModel _vm;

    public MainViewModelContextMenuTests()
    {
        _repoRoot = Path.Combine(Path.GetTempPath(), "DiffViewerCtx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repoRoot);

        _repo = new FakeRepositoryService(_repoRoot);
        _git = new FakeGitWriteService();
        _launcher = new FakeExternalAppLauncher();
        _settings = new InMemorySettingsService();

        _vm = new MainViewModel(
            repository: _repo,
            left: new DiffSide.WorkingTree(),
            right: new DiffSide.CommitIsh("HEAD"),
            settingsService: _settings,
            gitWriteService: _git,
            externalAppLauncher: _launcher);
    }

    public void Dispose()
    {
        _vm.Dispose();
        try { Directory.Delete(_repoRoot, recursive: true); } catch { /* best effort */ }
    }

    private FileEntryViewModel UntrackedRow(string path) => new(
        new FileChange(
            Path: path,
            OldPath: null,
            Status: FileStatus.Added,
            ConflictCode: null,
            Layer: WorkingTreeLayer.Untracked,
            LeftBlobSha: null, RightBlobSha: "abcdef",
            IsBinary: false,
            LeftFileSizeBytes: null, RightFileSizeBytes: 12,
            IsLfsPointer: false, IsSparseNotCheckedOut: false,
            OldMode: 0, NewMode: 33188 /* 0o100644 */),
        _repoRoot);

    private FileEntryViewModel ModifiedRow(string path) => new(
        new FileChange(
            Path: path,
            OldPath: null,
            Status: FileStatus.Modified,
            ConflictCode: null,
            Layer: WorkingTreeLayer.Unstaged,
            LeftBlobSha: "feedbabe", RightBlobSha: "deadbeef",
            IsBinary: false,
            LeftFileSizeBytes: 12, RightFileSizeBytes: 24,
            IsLfsPointer: false, IsSparseNotCheckedOut: false,
            OldMode: 33188 /* 0o100644 */, NewMode: 33188 /* 0o100644 */),
        _repoRoot);

    [Fact]
    public void StageFile_Untracked_ForwardsRepoRootAndPath()
    {
        var entry = UntrackedRow("src/new.cs");

        _vm.StageFileCommand.Execute(entry);

        _git.Calls.Should().ContainSingle();
        _git.Calls[0].Should().Be(("StageFile", _repoRoot, "src/new.cs"));
    }

    [Fact]
    public void DeleteFile_WhenSuppressionOff_PromptsAndAborts_OnCancel()
    {
        var entry = UntrackedRow("garbage.tmp");
        _vm.ConfirmHandler = _ => ConfirmationResult.Cancel();

        _vm.DeleteFileCommand.Execute(entry);

        _git.Calls.Should().BeEmpty("user cancelled the prompt");
    }

    [Fact]
    public void DeleteFile_WhenSuppressionOff_RunsAndPersistsPreference_OnConfirmWithDontAskAgain()
    {
        var entry = UntrackedRow("garbage.tmp");
        ConfirmationRequest? seen = null;
        _vm.ConfirmHandler = req =>
        {
            seen = req;
            return ConfirmationResult.Yes(dontAskAgain: true);
        };

        _vm.DeleteFileCommand.Execute(entry);

        seen.Should().NotBeNull();
        seen!.ShowDontAskAgain.Should().BeTrue();
        seen.ConfirmText.Should().Be("Delete");

        _git.Calls.Should().ContainSingle();
        _git.Calls[0].Item1.Should().Be("DeleteToRecycleBin");

        _settings.Current.SuppressDeleteFileConfirmation.Should().BeTrue();
    }

    [Fact]
    public void DeleteFile_WhenSuppressionOn_SkipsPrompt()
    {
        _settings.Update(s => s with { SuppressDeleteFileConfirmation = true });
        var entry = UntrackedRow("garbage.tmp");
        _vm.ConfirmHandler = _ => throw new Xunit.Sdk.XunitException("Should not prompt when suppressed.");

        _vm.DeleteFileCommand.Execute(entry);

        _git.Calls.Should().ContainSingle();
        _git.Calls[0].Item1.Should().Be("DeleteToRecycleBin");
    }

    [Fact]
    public void AddToGitignore_ForwardsRelativePath()
    {
        var entry = UntrackedRow("logs/foo.log");

        _vm.AddToGitignoreCommand.Execute(entry);

        _git.Calls.Should().ContainSingle();
        _git.Calls[0].Should().Be(("AddToGitignore", _repoRoot, "logs/foo.log"));
    }

    [Fact]
    public void OpenInExternalEditor_PassesZeroLineForFileLevelInvocation()
    {
        var entry = ModifiedRow("src/foo.cs");

        _vm.OpenInExternalEditorCommand.Execute(entry);

        _launcher.LaunchEditorCalls.Should().ContainSingle();
        _launcher.LaunchEditorCalls[0].path.Should().Be(entry.FullPath);
        _launcher.LaunchEditorCalls[0].line.Should().Be(0);
    }

    [Fact]
    public void ShowInExplorer_ForwardsFullPath()
    {
        var entry = ModifiedRow("src/foo.cs");

        _vm.ShowInExplorerCommand.Execute(entry);

        _launcher.ShowInExplorerCalls.Should().ContainSingle();
        _launcher.ShowInExplorerCalls[0].Should().Be(entry.FullPath);
    }

    [Fact]
    public void OpenWithDefaultApp_ForwardsFullPath()
    {
        var entry = ModifiedRow("src/foo.cs");

        _vm.OpenWithDefaultAppCommand.Execute(entry);

        _launcher.OpenWithDefaultAppCalls.Should().ContainSingle();
        _launcher.OpenWithDefaultAppCalls[0].Should().Be(entry.FullPath);
    }

    [Fact]
    public void Copy_Commands_ForwardCorrectStrings()
    {
        var entry = ModifiedRow("src/sub/foo.cs");
        var grabbed = new List<string>();
        _vm.ClipboardWriter = grabbed.Add;

        _vm.CopyFileNameCommand.Execute(entry);
        _vm.CopyRepoRelativePathCommand.Execute(entry);
        _vm.CopyFullPathCommand.Execute(entry);
        _vm.CopyLeftBlobShaCommand.Execute(entry);
        _vm.CopyRightBlobShaCommand.Execute(entry);

        grabbed.Should().BeEquivalentTo(new[]
        {
            "foo.cs",
            entry.RepoRelativePath,
            entry.FullPath,
            "feedbabe",
            "deadbeef",
        }, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void Commands_NoOp_OnNullEntry()
    {
        _vm.StageFileCommand.Execute(null);
        _vm.DeleteFileCommand.Execute(null);
        _vm.OpenInExternalEditorCommand.Execute(null);

        _git.Calls.Should().BeEmpty();
        _launcher.LaunchEditorCalls.Should().BeEmpty();
    }

    [Fact]
    public void GitWriteFailure_SurfacedToToastHandler()
    {
        var toasts = new List<string>();
        _vm.ToastHandler = toasts.Add;
        _git.NextResult = GitWriteResult.Fail(128, "fatal: pathspec");
        var entry = UntrackedRow("nope.txt");

        _vm.StageFileCommand.Execute(entry);

        toasts.Should().ContainSingle()
            .Which.Should().Contain("Stage failed").And.Contain("fatal: pathspec");
    }

    // ------------- Diff-pane hunk commands -------------
    //
    // These tests exercise commands that resolve their target hunk through
    // DiffPane.HunkAtLine, which requires a populated DiffPaneViewModel. We
    // construct a fresh VM inside a UI sync context (LoadAsync continues on
    // TaskScheduler.FromCurrentSynchronizationContext) and drive the commands
    // there. The command invocations themselves are .Execute(...) on the
    // already-populated VM, so ConfigureAwait(false) inside the command does
    // not cause a deadlock.

    [Fact]
    public async Task StageHunkAtCaret_ForwardsHunkInputsToGit()
    {
        var repo = new FakeRepositoryService(_repoRoot)
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha\ngamma\n",
        };
        var git = new FakeGitWriteService();

        await RunOnUiSyncContextAsync(async vm =>
        {
            await vm.DiffPane.LoadAsync(ModifiedRow("a.cs"));
            var hunk = vm.DiffPane.CurrentHunks.First();
            vm.StageHunkAtCaretCommand.Execute(
                new HunkActionContext(ChangeSide.Right, hunk.NewStartLine));
        }, repo, git);

        git.Calls.Should().ContainSingle();
        git.Calls[0].Op.Should().Be(nameof(GitWriteOperationKind.StageHunk));
        git.Calls[0].Path.Should().Be("a.cs");
    }

    [Fact]
    public async Task UnstageHunkAtCaret_ForwardsHunkInputsToGit()
    {
        var repo = new FakeRepositoryService(_repoRoot)
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha\ngamma\n",
        };
        var git = new FakeGitWriteService();

        await RunOnUiSyncContextAsync(async vm =>
        {
            await vm.DiffPane.LoadAsync(ModifiedRow("a.cs"));
            var hunk = vm.DiffPane.CurrentHunks.First();
            vm.UnstageHunkAtCaretCommand.Execute(
                new HunkActionContext(ChangeSide.Right, hunk.NewStartLine));
        }, repo, git);

        git.Calls.Should().ContainSingle();
        git.Calls[0].Op.Should().Be(nameof(GitWriteOperationKind.UnstageHunk));
    }

    [Fact]
    public async Task RevertHunkAtCaret_WhenSuppressionOff_PromptsAndAborts_OnCancel()
    {
        var repo = new FakeRepositoryService(_repoRoot)
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha\ngamma\n",
        };
        var git = new FakeGitWriteService();

        await RunOnUiSyncContextAsync(async vm =>
        {
            vm.ConfirmHandler = _ => ConfirmationResult.Cancel();
            await vm.DiffPane.LoadAsync(ModifiedRow("a.cs"));
            var hunk = vm.DiffPane.CurrentHunks.First();
            vm.RevertHunkAtCaretCommand.Execute(
                new HunkActionContext(ChangeSide.Right, hunk.NewStartLine));
        }, repo, git);

        git.Calls.Should().BeEmpty("user cancelled the revert prompt");
    }

    [Fact]
    public async Task RevertHunkAtCaret_WhenSuppressionOff_RunsAndPersistsPreference_OnConfirm()
    {
        var repo = new FakeRepositoryService(_repoRoot)
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha\ngamma\n",
        };
        var git = new FakeGitWriteService();
        var settings = new InMemorySettingsService();
        ConfirmationRequest? seen = null;

        await RunOnUiSyncContextAsync(async vm =>
        {
            vm.ConfirmHandler = req =>
            {
                seen = req;
                return ConfirmationResult.Yes(dontAskAgain: true);
            };
            await vm.DiffPane.LoadAsync(ModifiedRow("a.cs"));
            var hunk = vm.DiffPane.CurrentHunks.First();
            vm.RevertHunkAtCaretCommand.Execute(
                new HunkActionContext(ChangeSide.Right, hunk.NewStartLine));
        }, repo, git, settings);

        seen.Should().NotBeNull();
        seen!.ConfirmText.Should().Be("Revert");
        git.Calls.Should().ContainSingle();
        git.Calls[0].Op.Should().Be(nameof(GitWriteOperationKind.RevertHunk));
        settings.Current.SuppressRevertHunkConfirmation.Should().BeTrue();
    }

    [Fact]
    public async Task RevertHunkAtCaret_PromptDescribesHunkRatherThanPreviewingLines()
    {
        // The dialog used to show a 3-line preview from the top of the hunk
        // - which, because DiffService prepends context lines around every
        // hunk, almost always rendered three unchanged lines and zero
        // indication of what was about to be discarded. The replacement is
        // a one-line summary of additions/removals + line number so the
        // user knows what they're confirming without dragging editor
        // settings into a plain System.Windows MessageBox-style dialog.
        var repo = new FakeRepositoryService(_repoRoot)
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha\ngamma\n",
        };
        var git = new FakeGitWriteService();
        ConfirmationRequest? seen = null;

        await RunOnUiSyncContextAsync(async vm =>
        {
            vm.ConfirmHandler = req =>
            {
                seen = req;
                return ConfirmationResult.Cancel();
            };
            await vm.DiffPane.LoadAsync(ModifiedRow("a.cs"));
            var hunk = vm.DiffPane.CurrentHunks.First();
            vm.RevertHunkAtCaretCommand.Execute(
                new HunkActionContext(ChangeSide.Right, hunk.NewStartLine));
        }, repo, git);

        seen.Should().NotBeNull();
        seen!.Message.Should().Contain("1 addition", "the inserted 'gamma' line should be counted");
        seen.Message.Should().Contain("1 removal", "the deleted 'beta' line should be counted");
        seen.Message.Should().Contain("at line 2",
            "the prompt should pin the change to its line number in the working tree");
        seen.Message.Should().NotContain("Preview:",
            "the old line-by-line preview has been removed");
        seen.Message.Should().NotContain("alpha",
            "context lines should not leak into the summary");
    }

    [Fact]
    public async Task RevertHunkAtCaret_WhenSuppressionOn_SkipsPrompt()
    {
        var repo = new FakeRepositoryService(_repoRoot)
        {
            LeftText = "alpha\nbeta\n",
            RightText = "alpha\ngamma\n",
        };
        var git = new FakeGitWriteService();
        var settings = new InMemorySettingsService();
        settings.Update(s => s with { SuppressRevertHunkConfirmation = true });

        await RunOnUiSyncContextAsync(async vm =>
        {
            vm.ConfirmHandler = _ => throw new Xunit.Sdk.XunitException("Should not prompt when suppressed.");
            await vm.DiffPane.LoadAsync(ModifiedRow("a.cs"));
            var hunk = vm.DiffPane.CurrentHunks.First();
            vm.RevertHunkAtCaretCommand.Execute(
                new HunkActionContext(ChangeSide.Right, hunk.NewStartLine));
        }, repo, git, settings);

        git.Calls.Should().ContainSingle();
        git.Calls[0].Op.Should().Be(nameof(GitWriteOperationKind.RevertHunk));
    }

    [Fact]
    public async Task HunkCommands_NoOp_OnContextLine()
    {
        var leftMid = string.Concat(Enumerable.Range(2, 14).Select(i => $"line{i}\n"));
        var rightMid = string.Concat(Enumerable.Range(2, 14).Select(i => $"line{i}\n"));
        var repo = new FakeRepositoryService(_repoRoot)
        {
            LeftText = "one\n" + leftMid + "sixteen\n",
            RightText = "ONE\n" + rightMid + "SIXTEEN\n",
        };
        var git = new FakeGitWriteService();

        await RunOnUiSyncContextAsync(async vm =>
        {
            await vm.DiffPane.LoadAsync(ModifiedRow("a.cs"));
            // Line 8 is in the middle context; HunkAtLine returns null,
            // TryGetHunkAction returns false, no git call is made.
            vm.StageHunkAtCaretCommand.Execute(new HunkActionContext(ChangeSide.Right, 8));
            vm.UnstageHunkAtCaretCommand.Execute(new HunkActionContext(ChangeSide.Right, 8));
            vm.RevertHunkAtCaretCommand.Execute(new HunkActionContext(ChangeSide.Right, 8));
        }, repo, git);

        git.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenInExternalEditorAtLine_ForwardsLineToLauncher()
    {
        var repo = new FakeRepositoryService(_repoRoot);
        var git = new FakeGitWriteService();
        var launcher = new FakeExternalAppLauncher();
        var entry = ModifiedRow("src/foo.cs");

        await RunOnUiSyncContextAsync(vm =>
        {
            vm.OpenInExternalEditorAtLineCommand.Execute(new LineActionContext(entry, 42));
            return Task.CompletedTask;
        }, repo, git, launcher: launcher);

        launcher.LaunchEditorCalls.Should().ContainSingle();
        launcher.LaunchEditorCalls[0].path.Should().Be(entry.FullPath);
        launcher.LaunchEditorCalls[0].line.Should().Be(42);
    }

    /// <summary>
    /// Construct a fresh MainViewModel inside a UI sync context (for
    /// LoadAsync's TaskScheduler.FromCurrentSynchronizationContext
    /// continuation), then run the body. Mirrors the helper in
    /// DiffPaneViewModelTests.
    /// </summary>
    private static async Task RunOnUiSyncContextAsync(
        Func<MainViewModel, Task> body,
        FakeRepositoryService repo,
        FakeGitWriteService git,
        InMemorySettingsService? settings = null,
        FakeExternalAppLauncher? launcher = null)
    {
        var tcs = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            System.Threading.SynchronizationContext.SetSynchronizationContext(
                new System.Windows.Threading.DispatcherSynchronizationContext(
                    System.Windows.Threading.Dispatcher.CurrentDispatcher));
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
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
                        settingsService: settings ?? new InMemorySettingsService(),
                        gitWriteService: git,
                        externalAppLauncher: launcher ?? new FakeExternalAppLauncher());
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
            System.Windows.Threading.Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await tcs.Task;
    }

    // ------------- Test fakes -------------

    private sealed class FakeRepositoryService : IRepositoryService
    {
        public FakeRepositoryService(string repoRoot) =>
            Shape = new RepositoryShape(
                RepoRoot: repoRoot,
                WorkingDirectory: repoRoot,
                GitDir: System.IO.Path.Combine(repoRoot, ".git"),
                IsBare: false,
                IsHeadUnborn: false,
                IsSparseCheckout: false,
                IsPartialClone: false,
                HasInProgressOperation: false);

        public RepositoryShape Shape { get; }

#pragma warning disable CS0067 // ChangeListUpdated/RepositoryLost are interface-required; tests don't raise them.
        public event EventHandler<ChangeListUpdatedEventArgs>? ChangeListUpdated;
        public event EventHandler<RepositoryLostEventArgs>? RepositoryLost;
#pragma warning restore CS0067

        public IReadOnlyList<FileChange> CurrentChanges { get; private set; } = Array.Empty<FileChange>();

        public string LeftText { get; set; } = string.Empty;
        public string RightText { get; set; } = string.Empty;

        public string? ResolveCommitIsh(string reference) => "0".PadLeft(40, '0');
        public bool ValidateRevisions(string leftRef, string rightRef) => true;

        public IReadOnlyList<FileChange> EnumerateChanges(DiffSide left, DiffSide right) => CurrentChanges;

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
            return (CurrentChanges, new Sub(this, handler));
        }

        public bool IsPathIgnored(string repoRelativeForwardSlashPath) => false;

        public void Dispose() { }

        private sealed class Sub : IDisposable
        {
            private readonly FakeRepositoryService _owner;
            private readonly EventHandler<ChangeListUpdatedEventArgs> _h;
            public Sub(FakeRepositoryService owner, EventHandler<ChangeListUpdatedEventArgs> h)
            {
                _owner = owner; _h = h;
            }
            public void Dispose() => _owner.ChangeListUpdated -= _h;
        }
    }

    private sealed class FakeGitWriteService : IGitWriteService
    {
        public List<(string Op, string RepoRoot, string Path)> Calls { get; } = new();
        public GitWriteResult NextResult { get; set; } = GitWriteResult.Ok();

        public event EventHandler<GitWriteOperationEventArgs>? BeforeOperation;
        public event EventHandler<GitWriteOperationEventArgs>? AfterOperation;

        private GitWriteResult Run(GitWriteOperationKind kind, string repoRoot, string path)
        {
            Calls.Add((kind.ToString(), repoRoot, path));
            var args = new GitWriteOperationEventArgs { Kind = kind, FilePath = path };
            BeforeOperation?.Invoke(this, args);
            var result = NextResult;
            AfterOperation?.Invoke(
                this,
                new GitWriteOperationEventArgs
                {
                    OperationId = args.OperationId,
                    Kind = kind,
                    FilePath = path,
                    Result = result,
                });
            return result;
        }

        public Task<GitWriteResult> StageFileAsync(string repoPath, string filePath, CancellationToken ct = default) =>
            Task.FromResult(Run(GitWriteOperationKind.StageFile, repoPath, filePath));

        public Task<GitWriteResult> UnstageFileAsync(string repoPath, string filePath, CancellationToken ct = default) =>
            Task.FromResult(Run(GitWriteOperationKind.UnstageFile, repoPath, filePath));

        public Task<GitWriteResult> StageHunkAsync(string repoPath, HunkPatchInputs inputs, CancellationToken ct = default) =>
            Task.FromResult(Run(GitWriteOperationKind.StageHunk, repoPath, inputs.FilePath));

        public Task<GitWriteResult> UnstageHunkAsync(string repoPath, HunkPatchInputs inputs, CancellationToken ct = default) =>
            Task.FromResult(Run(GitWriteOperationKind.UnstageHunk, repoPath, inputs.FilePath));

        public Task<GitWriteResult> RevertHunkAsync(string repoPath, HunkPatchInputs inputs, CancellationToken ct = default) =>
            Task.FromResult(Run(GitWriteOperationKind.RevertHunk, repoPath, inputs.FilePath));

        public Task<GitWriteResult> AddToGitignoreAsync(string repoPath, string repoRelativePath, CancellationToken ct = default) =>
            Task.FromResult(Run(GitWriteOperationKind.AddToGitignore, repoPath, repoRelativePath));

        public Task<GitWriteResult> DeleteToRecycleBinAsync(string repoPath, string filePath, CancellationToken ct = default) =>
            Task.FromResult(Run(GitWriteOperationKind.DeleteToRecycleBin, repoPath, filePath));
    }

    private sealed class FakeExternalAppLauncher : IExternalAppLauncher
    {
        public List<(string path, int line)> LaunchEditorCalls { get; } = new();
        public List<string> ShowInExplorerCalls { get; } = new();
        public List<string> OpenWithDefaultAppCalls { get; } = new();

        public Task<LaunchResult> LaunchEditorAsync(string filePath, int line = 0)
        {
            LaunchEditorCalls.Add((filePath, line));
            return Task.FromResult(LaunchResult.Ok());
        }

        public LaunchResult ShowInExplorer(string filePath)
        {
            ShowInExplorerCalls.Add(filePath);
            return LaunchResult.Ok();
        }

        public LaunchResult OpenWithDefaultApp(string filePath)
        {
            OpenWithDefaultAppCalls.Add(filePath);
            return LaunchResult.Ok();
        }

        public EditorResolution ResolveEditor(bool forceReDetect = false) =>
            new(null, EditorFamily.None, null);
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        private AppSettings _current;
        public InMemorySettingsService() : this(new AppSettings()) { }
        public InMemorySettingsService(AppSettings initial) { _current = initial; }
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
