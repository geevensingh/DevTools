namespace DiffViewer.Models;

/// <summary>
/// Canonical representation of one diff hunk. All <see cref="DiffViewer.Services.DiffService"/>
/// callers (renderer, "Copy diff", <c>git apply</c> patch builder) consume this
/// type — DiffPlex's internal types never leak past <c>DiffService</c>.
/// </summary>
/// <param name="OldStartLine">1-based line number in the old buffer where this hunk begins.</param>
/// <param name="OldLineCount">
/// Number of old-buffer lines covered by this hunk, <b>including the surrounding
/// context lines added by <see cref="DiffViewer.Services.DiffService"/>.</b> So
/// even a pure-insert hunk reports a non-zero count when its inserted region
/// sits between context lines — callers that need the "real" insert/delete
/// counts must filter <see cref="DiffLine"/>s by <see cref="DiffLineKind"/>.
/// </param>
/// <param name="NewStartLine">1-based line number in the new buffer where this hunk begins.</param>
/// <param name="NewLineCount">
/// Number of new-buffer lines covered by this hunk, <b>including the surrounding
/// context lines added by <see cref="DiffViewer.Services.DiffService"/>.</b>
/// See <see cref="OldLineCount"/> for filtering guidance.
/// </param>
/// <param name="Lines">Ordered context/insert/delete/modified lines.</param>
/// <param name="FunctionContext">Optional "@@ ... @@" trailer (function/section name); <c>null</c> if unknown.</param>
public sealed record DiffHunk(
    int OldStartLine,
    int OldLineCount,
    int NewStartLine,
    int NewLineCount,
    IReadOnlyList<DiffLine> Lines,
    string? FunctionContext);
