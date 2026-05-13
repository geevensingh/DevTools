using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Computes line- and word-level diffs between two text buffers and renders
/// them as unified-diff text. DiffPlex's internal types never leak past this
/// interface — see <see cref="DiffViewer.Models.DiffHunk"/> for the canonical
/// hunk shape that callers consume.
/// </summary>
public interface IDiffService
{
    /// <summary>
    /// Compute the full hunk model under the supplied options. May fall
    /// back to a line-only diff for inputs above the configured threshold —
    /// see <see cref="DiffComputation.FallbackReason"/>.
    /// </summary>
    DiffComputation ComputeDiff(string left, string right, DiffOptions options);

    /// <summary>
    /// Fast-path: returns true at the first non-context difference between
    /// the two inputs under the supplied options. Used by the pre-diff pass
    /// to decide whether to flag a row as <em>whitespace-only</em>.
    /// </summary>
    bool HasVisibleDifferences(string left, string right, DiffOptions options);

    /// <summary>
    /// Format a previously-computed hunk sequence as a unified-diff string.
    /// The byte-exact line endings of <paramref name="leftSource"/> and
    /// <paramref name="rightSource"/> are preserved on every emitted line so
    /// the result applies cleanly via <c>git apply</c>.
    /// </summary>
    string FormatUnified(
        string oldPath,
        string newPath,
        IReadOnlyList<DiffHunk> hunks,
        string leftSource,
        string rightSource);

    /// <summary>
    /// Compute word-level intra-line diff between two lines of text. Used by
    /// the renderer to highlight which words inside a modified line changed.
    /// Returns a list of pieces, each marked as inserted / deleted / unchanged.
    /// </summary>
    IReadOnlyList<IntraLinePiece> ComputeIntraLineDiff(string oldLine, string newLine, bool ignoreWhitespace);
}

/// <summary>One word-or-whitespace span inside an intra-line diff.</summary>
public sealed record IntraLinePiece(IntraLinePieceKind Kind, string Text);

public enum IntraLinePieceKind
{
    Unchanged,
    Inserted,
    Deleted,
}
