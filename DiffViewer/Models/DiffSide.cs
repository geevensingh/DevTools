namespace DiffViewer.Models;

/// <summary>
/// One of the two sides being compared. Either a fixed commit-ish reference
/// (SHA, branch, tag, <c>HEAD~3</c>, ...) or the live working tree.
/// </summary>
/// <remarks>
/// The working tree itself decomposes into staged / unstaged / untracked
/// layers — that decomposition lives in <see cref="WorkingTreeLayer"/> and
/// is applied by the repository service when it enumerates changes; a
/// <see cref="DiffSide"/> only says "use the working tree", not which
/// layer.
/// </remarks>
public abstract record DiffSide
{
    /// <summary>A fixed commit-ish reference (SHA, branch, tag, <c>HEAD~3</c>).</summary>
    public sealed record CommitIsh(string Reference) : DiffSide
    {
        public override string ToString() => Reference;
    }

    /// <summary>The live working tree (with all its layers).</summary>
    public sealed record WorkingTree : DiffSide
    {
        public override string ToString() => "<working-tree>";
    }

    /// <summary>True if either side of a comparison points at the working tree.</summary>
    public bool IsWorkingTree => this is WorkingTree;

    private DiffSide() { }
}
