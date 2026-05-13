using System;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// All git write operations in DiffViewer flow through this service. Reads
/// stay on <see cref="IRepositoryService"/> (LibGit2Sharp); every mutation
/// shells out to <c>git.exe</c> so we inherit the user's git config
/// behaviour (line-ending normalisation, hooks, custom diff drivers) for
/// free.
///
/// <para>Callers should listen to <see cref="BeforeOperation"/> /
/// <see cref="AfterOperation"/> to suspend the repo watcher and refresh
/// the LibGit2Sharp index cache around the write — see the plan's
/// "Watcher coordination" section.</para>
/// </summary>
public interface IGitWriteService
{
    /// <summary>Raised before a write operation runs <c>git.exe</c>.</summary>
    event EventHandler<GitWriteOperationEventArgs>? BeforeOperation;

    /// <summary>Raised after a write operation completes (success or failure).</summary>
    event EventHandler<GitWriteOperationEventArgs>? AfterOperation;

    /// <summary>
    /// Stage the <paramref name="inputs"/> hunk into the index via
    /// <c>git apply --cached --recount --whitespace=nowarn</c>.
    /// </summary>
    Task<GitWriteResult> StageHunkAsync(string repoPath, HunkPatchInputs inputs, CancellationToken ct = default);

    /// <summary>
    /// Reverse-apply the <paramref name="inputs"/> hunk against the index via
    /// <c>git apply --cached --reverse --recount --whitespace=nowarn</c>.
    /// Effectively unstages a previously-staged hunk.
    /// </summary>
    Task<GitWriteResult> UnstageHunkAsync(string repoPath, HunkPatchInputs inputs, CancellationToken ct = default);

    /// <summary>
    /// Reverse-apply the <paramref name="inputs"/> hunk against the working
    /// tree via <c>git apply --reverse --recount --whitespace=nowarn</c>.
    /// Discards an unstaged change. <b>Destructive.</b>
    /// </summary>
    Task<GitWriteResult> RevertHunkAsync(string repoPath, HunkPatchInputs inputs, CancellationToken ct = default);

    /// <summary>
    /// <c>git -C &lt;repo&gt; add -- &lt;path&gt;</c>. Used both to start
    /// tracking an untracked file and to whole-file-stage a tracked
    /// modification.
    /// </summary>
    Task<GitWriteResult> StageFileAsync(string repoPath, string filePath, CancellationToken ct = default);

    /// <summary>
    /// <c>git -C &lt;repo&gt; restore --staged -- &lt;path&gt;</c>. Removes
    /// any staged change on the path, leaving the working tree alone.
    /// </summary>
    Task<GitWriteResult> UnstageFileAsync(string repoPath, string filePath, CancellationToken ct = default);

    /// <summary>
    /// Append the <paramref name="repoRelativePath"/> to the repo's root
    /// <c>.gitignore</c> (creating it if needed). Path is normalised to
    /// forward slashes, gitignore-significant characters are escaped, and
    /// the entry is anchored to the repo root with a leading <c>/</c>.
    /// Idempotent.
    /// </summary>
    Task<GitWriteResult> AddToGitignoreAsync(string repoPath, string repoRelativePath, CancellationToken ct = default);

    /// <summary>
    /// Send <paramref name="filePath"/> to the Recycle Bin via
    /// <c>SHFileOperation</c>. Not a git op; recoverable from the bin.
    /// <b>Destructive.</b>
    /// </summary>
    Task<GitWriteResult> DeleteToRecycleBinAsync(string repoPath, string filePath, CancellationToken ct = default);
}

/// <summary>Result of a single <see cref="IGitWriteService"/> call.</summary>
/// <param name="Success"><c>true</c> iff the underlying git command exited 0.</param>
/// <param name="ExitCode">Process exit code (0 on success, &gt;0 on failure, -1 if not started).</param>
/// <param name="StdOut">Captured stdout text (may be empty).</param>
/// <param name="StdErr">Captured stderr text (surfaced verbatim to the user on failure).</param>
public sealed record GitWriteResult(bool Success, int ExitCode, string StdOut, string StdErr)
{
    public static GitWriteResult Ok(string stdOut = "") => new(true, 0, stdOut, "");
    public static GitWriteResult Fail(int exitCode, string stdErr, string stdOut = "") => new(false, exitCode, stdOut, stdErr);
}

/// <summary>
/// Inputs needed to build a per-hunk unified-diff patch for
/// <c>git apply</c>. Carries both source buffers so
/// <see cref="DiffViewer.Utility.UnifiedDiffFormatter"/> can preserve
/// byte-exact line terminators.
/// </summary>
/// <param name="FilePath">Repo-relative path with forward slashes; used in the <c>--- a/</c> + <c>+++ b/</c> headers.</param>
/// <param name="Hunk">The hunk to stage / unstage / revert.</param>
/// <param name="LeftSource">Full text of the left-side (old) blob.</param>
/// <param name="RightSource">Full text of the right-side (new) blob.</param>
public sealed record HunkPatchInputs(
    string FilePath,
    DiffHunk Hunk,
    string LeftSource,
    string RightSource);

public sealed class GitWriteOperationEventArgs : EventArgs
{
    /// <summary>
    /// Unique per-operation identifier. Subscribers use it to pair the
    /// matching <see cref="IGitWriteService.BeforeOperation"/> and
    /// <see cref="IGitWriteService.AfterOperation"/> events (the same Id
    /// fires on both) - critical when nested ops can race in flight.
    /// </summary>
    public Guid OperationId { get; init; } = Guid.NewGuid();
    public GitWriteOperationKind Kind { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public GitWriteResult? Result { get; init; }
}

public enum GitWriteOperationKind
{
    StageHunk,
    UnstageHunk,
    RevertHunk,
    StageFile,
    UnstageFile,
    AddToGitignore,
    DeleteToRecycleBin,
}
