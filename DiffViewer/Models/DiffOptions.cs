namespace DiffViewer.Models;

/// <summary>
/// Options that control diff computation. Held immutably so callers can
/// reason about whether the result they have is current under whatever the
/// toolbar shows now.
/// </summary>
/// <param name="IgnoreWhitespace">If true, lines are compared with whitespace collapsed.</param>
/// <param name="ContextLines">Number of unchanged context lines to include around each hunk in <see cref="DiffHunk"/>. Default is 3 (git's default).</param>
/// <param name="MaxLines">Per-side line-count cap; above this, <see cref="DiffViewer.Services.IDiffService"/> falls back to a line-only diff. Default 50,000.</param>
/// <param name="TimeoutMs">Per-call wall-clock timeout in ms; on timeout, <see cref="DiffViewer.Services.IDiffService"/> falls back to a line-only diff. Default 3,000.</param>
public sealed record DiffOptions(
    bool IgnoreWhitespace = false,
    int ContextLines = 3,
    int MaxLines = 50_000,
    int TimeoutMs = 3_000);

/// <summary>
/// Why the line-only fallback path was used (or <see cref="None"/> if it was not).
/// </summary>
public enum DiffFallbackReason
{
    None,
    InputTooLarge,
    Timeout,
}

/// <summary>
/// Output of <see cref="DiffViewer.Services.IDiffService.ComputeDiff"/>.
/// </summary>
public sealed record DiffComputation(
    IReadOnlyList<DiffHunk> Hunks,
    DiffFallbackReason FallbackReason);
