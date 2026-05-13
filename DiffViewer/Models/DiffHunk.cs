namespace DiffViewer.Models;

/// <summary>
/// Canonical representation of one diff hunk. All <see cref="DiffViewer.Services.DiffService"/>
/// callers (renderer, "Copy diff", <c>git apply</c> patch builder) consume this
/// type — DiffPlex's internal types never leak past <c>DiffService</c>.
/// </summary>
/// <param name="OldStartLine">1-based line number in the old buffer where this hunk begins.</param>
/// <param name="OldLineCount">Number of old-buffer lines covered by this hunk (0 for pure-insert hunks).</param>
/// <param name="NewStartLine">1-based line number in the new buffer where this hunk begins.</param>
/// <param name="NewLineCount">Number of new-buffer lines covered by this hunk (0 for pure-delete hunks).</param>
/// <param name="Lines">Ordered context/insert/delete/modified lines.</param>
/// <param name="FunctionContext">Optional "@@ ... @@" trailer (function/section name); <c>null</c> if unknown.</param>
public sealed record DiffHunk(
    int OldStartLine,
    int OldLineCount,
    int NewStartLine,
    int NewLineCount,
    IReadOnlyList<DiffLine> Lines,
    string? FunctionContext);
