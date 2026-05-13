namespace DiffViewer.Models;

/// <summary>
/// Static facts about a repository captured at open time. Drives the
/// command-line dispatch (e.g. reject working-tree input modes against a
/// bare repo) and gates expensive features like the eager pre-diff pass
/// on partial-clone repos.
/// </summary>
/// <param name="RepoRoot">Absolute path to the repo root (or the <c>.git</c> dir for bare repos).</param>
/// <param name="WorkingDirectory">Absolute path to the working tree, or <c>null</c> for bare repos.</param>
/// <param name="GitDir">Absolute path to the actual <c>.git</c> dir (resolves linked-worktree pointers).</param>
/// <param name="IsBare">True if the repo is bare (no working tree).</param>
/// <param name="IsHeadUnborn">True if there are no commits yet.</param>
/// <param name="IsSparseCheckout">True if <c>core.sparseCheckout=true</c>.</param>
/// <param name="IsPartialClone">True if any remote has <c>promisor=true</c>.</param>
/// <param name="HasInProgressOperation">True if a merge / rebase / cherry-pick / revert / stash-pop is in progress.</param>
public sealed record RepositoryShape(
    string RepoRoot,
    string? WorkingDirectory,
    string GitDir,
    bool IsBare,
    bool IsHeadUnborn,
    bool IsSparseCheckout,
    bool IsPartialClone,
    bool HasInProgressOperation);
