using DiffPlex;
using DiffPlex.Chunkers;
using DiffPlex.Model;
using DiffViewer.Models;
using DiffViewer.Utility;

namespace DiffViewer.Services;

/// <summary>
/// Default <see cref="IDiffService"/>. Wraps DiffPlex for line- and
/// word-level diff and converts <see cref="DiffPlex.Model.DiffBlock"/> into
/// the canonical <see cref="DiffHunk"/> model up-front so DiffPlex types
/// never escape this class.
/// </summary>
public sealed class DiffService : IDiffService
{
    private readonly IDiffer _differ;

    public DiffService(IDiffer? differ = null)
    {
        _differ = differ ?? Differ.Instance;
    }

    public DiffComputation ComputeDiff(string left, string right, DiffOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        left ??= string.Empty;
        right ??= string.Empty;

        var leftLines = LineSplitter.Split(left);
        var rightLines = LineSplitter.Split(right);

        DiffFallbackReason fallback = DiffFallbackReason.None;

        if (leftLines.Count > options.MaxLines || rightLines.Count > options.MaxLines)
        {
            fallback = DiffFallbackReason.InputTooLarge;
            return new DiffComputation(BuildHunks(leftLines, rightLines, options), fallback);
        }

        var hunks = RunWithTimeout(
            () => BuildHunks(leftLines, rightLines, options),
            options.TimeoutMs,
            out bool timedOut);

        if (timedOut)
        {
            fallback = DiffFallbackReason.Timeout;
            return new DiffComputation(BuildHunks(leftLines, rightLines, options), fallback);
        }

        return new DiffComputation(hunks, fallback);
    }

    public bool HasVisibleDifferences(string left, string right, DiffOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        left ??= string.Empty;
        right ??= string.Empty;

        // Equal strings are equal regardless of options — fastest path.
        if (ReferenceEquals(left, right) || left == right)
        {
            return false;
        }

        var leftLines = LineSplitter.Split(left);
        var rightLines = LineSplitter.Split(right);
        var (leftCmp, rightCmp) = BuildComparisonText(leftLines, rightLines, options);

        var result = _differ.CreateDiffs(leftCmp, rightCmp, ignoreWhiteSpace: false, ignoreCase: false, new LineChunker());
        return result.DiffBlocks.Count > 0;
    }

    public string FormatUnified(
        string oldPath,
        string newPath,
        IReadOnlyList<DiffHunk> hunks,
        string leftSource,
        string rightSource)
    {
        return UnifiedDiffFormatter.Format(oldPath, newPath, hunks, leftSource, rightSource);
    }

    public IReadOnlyList<IntraLinePiece> ComputeIntraLineDiff(string oldLine, string newLine, bool ignoreWhitespace)
    {
        oldLine ??= string.Empty;
        newLine ??= string.Empty;

        var result = _differ.CreateDiffs(
            oldLine,
            newLine,
            ignoreWhitespace,
            ignoreCase: false,
            new DelimiterChunker(new[] { ' ', '\t', '.', ',', ';', ':', '(', ')', '[', ']', '{', '}', '<', '>', '"', '\'' }));

        var pieces = new List<IntraLinePiece>(capacity: result.PiecesNew.Length + result.PiecesOld.Length);
        int oldIdx = 0;
        int newIdx = 0;

        var blocks = result.DiffBlocks
            .OrderBy(b => b.InsertStartB)
            .ToList();

        foreach (var block in blocks)
        {
            // Unchanged section before this block (from new side).
            while (newIdx < block.InsertStartB)
            {
                pieces.Add(new IntraLinePiece(IntraLinePieceKind.Unchanged, result.PiecesNew[newIdx]));
                newIdx++;
                oldIdx++;
            }

            // Deletes from this block.
            for (int i = 0; i < block.DeleteCountA; i++)
            {
                pieces.Add(new IntraLinePiece(IntraLinePieceKind.Deleted, result.PiecesOld[block.DeleteStartA + i]));
            }
            oldIdx = block.DeleteStartA + block.DeleteCountA;

            // Inserts from this block.
            for (int i = 0; i < block.InsertCountB; i++)
            {
                pieces.Add(new IntraLinePiece(IntraLinePieceKind.Inserted, result.PiecesNew[block.InsertStartB + i]));
            }
            newIdx = block.InsertStartB + block.InsertCountB;
        }

        // Trailing unchanged section.
        while (newIdx < result.PiecesNew.Length)
        {
            pieces.Add(new IntraLinePiece(IntraLinePieceKind.Unchanged, result.PiecesNew[newIdx]));
            newIdx++;
        }

        return pieces;
    }

    /// <summary>
    /// Convert DiffPlex's <see cref="DiffBlock"/> stream plus context lines
    /// into <see cref="DiffHunk"/> records. Adjacent or touching blocks
    /// (after expansion by <see cref="DiffOptions.ContextLines"/>) merge
    /// into one hunk, matching git's unified-diff convention.
    /// </summary>
    private List<DiffHunk> BuildHunks(
        IReadOnlyList<LineSplitter.Line> leftLines,
        IReadOnlyList<LineSplitter.Line> rightLines,
        DiffOptions options)
    {
        var (leftCmp, rightCmp) = BuildComparisonText(leftLines, rightLines, options);

        // ignoreWhiteSpace=false on DiffPlex — we already normalised the
        // comparison strings ourselves (see BuildComparisonText). Indices
        // returned in DiffBlocks therefore map 1:1 onto leftLines / rightLines.
        DiffResult result = _differ.CreateDiffs(leftCmp, rightCmp, ignoreWhiteSpace: false, ignoreCase: false, new LineChunker());

        var blocks = result.DiffBlocks.OrderBy(b => b.DeleteStartA).ToList();
        if (blocks.Count == 0)
        {
            return new List<DiffHunk>();
        }

        var rawHunks = new List<(int OldStart, int OldCount, int NewStart, int NewCount)>(blocks.Count);

        foreach (var block in blocks)
        {
            int oldStart = Math.Max(0, block.DeleteStartA - options.ContextLines);
            int oldEnd = Math.Min(leftLines.Count, block.DeleteStartA + block.DeleteCountA + options.ContextLines);
            int newStart = Math.Max(0, block.InsertStartB - options.ContextLines);
            int newEnd = Math.Min(rightLines.Count, block.InsertStartB + block.InsertCountB + options.ContextLines);

            rawHunks.Add((oldStart, oldEnd - oldStart, newStart, newEnd - newStart));
        }

        // Merge adjacent / overlapping hunks.
        var merged = new List<(int OldStart, int OldCount, int NewStart, int NewCount)>();
        foreach (var h in rawHunks)
        {
            if (merged.Count > 0)
            {
                var prev = merged[^1];
                int prevOldEnd = prev.OldStart + prev.OldCount;
                int prevNewEnd = prev.NewStart + prev.NewCount;
                if (h.OldStart <= prevOldEnd && h.NewStart <= prevNewEnd)
                {
                    int oldEnd = Math.Max(prevOldEnd, h.OldStart + h.OldCount);
                    int newEnd = Math.Max(prevNewEnd, h.NewStart + h.NewCount);
                    merged[^1] = (prev.OldStart, oldEnd - prev.OldStart, prev.NewStart, newEnd - prev.NewStart);
                    continue;
                }
            }

            merged.Add(h);
        }

        // For each merged hunk, walk both sides side-by-side honouring the
        // DiffPlex blocks that fall inside it, emitting context / delete / insert lines.
        var blocksQueue = new Queue<DiffBlock>(blocks);
        var hunks = new List<DiffHunk>(merged.Count);

        foreach (var h in merged)
        {
            var lines = new List<DiffLine>(capacity: h.OldCount + h.NewCount);

            int oldCursor = h.OldStart;       // 0-based
            int newCursor = h.NewStart;       // 0-based
            int oldHunkEnd = h.OldStart + h.OldCount;
            int newHunkEnd = h.NewStart + h.NewCount;

            while (oldCursor < oldHunkEnd || newCursor < newHunkEnd)
            {
                DiffBlock? nextBlock = blocksQueue.Count > 0 ? blocksQueue.Peek() : null;
                bool atBlockStart = nextBlock is { } b
                    && b.DeleteStartA == oldCursor
                    && b.InsertStartB == newCursor;

                if (atBlockStart)
                {
                    var block = blocksQueue.Dequeue();

                    // All deletes from this block first, then all inserts.
                    for (int i = 0; i < block.DeleteCountA; i++)
                    {
                        int oldIdx = block.DeleteStartA + i;
                        lines.Add(new DiffLine(
                            DiffLineKind.Deleted,
                            OldLineNumber: oldIdx + 1,
                            NewLineNumber: null,
                            Text: leftLines[oldIdx].Text));
                    }
                    oldCursor += block.DeleteCountA;

                    for (int i = 0; i < block.InsertCountB; i++)
                    {
                        int newIdx = block.InsertStartB + i;
                        lines.Add(new DiffLine(
                            DiffLineKind.Inserted,
                            OldLineNumber: null,
                            NewLineNumber: newIdx + 1,
                            Text: rightLines[newIdx].Text));
                    }
                    newCursor += block.InsertCountB;
                }
                else if (oldCursor < oldHunkEnd && newCursor < newHunkEnd)
                {
                    // Context line: same on both sides.
                    lines.Add(new DiffLine(
                        DiffLineKind.Context,
                        OldLineNumber: oldCursor + 1,
                        NewLineNumber: newCursor + 1,
                        Text: leftLines[oldCursor].Text));
                    oldCursor++;
                    newCursor++;
                }
                else if (oldCursor < oldHunkEnd)
                {
                    lines.Add(new DiffLine(
                        DiffLineKind.Deleted,
                        OldLineNumber: oldCursor + 1,
                        NewLineNumber: null,
                        Text: leftLines[oldCursor].Text));
                    oldCursor++;
                }
                else
                {
                    lines.Add(new DiffLine(
                        DiffLineKind.Inserted,
                        OldLineNumber: null,
                        NewLineNumber: newCursor + 1,
                        Text: rightLines[newCursor].Text));
                    newCursor++;
                }
            }

            hunks.Add(new DiffHunk(
                OldStartLine: h.OldStart + 1,
                OldLineCount: h.OldCount,
                NewStartLine: h.NewStart + 1,
                NewLineCount: h.NewCount,
                Lines: lines,
                FunctionContext: null));
        }

        return hunks;
    }

    /// <summary>
    /// Build the strings DiffPlex sees, derived from our own line splits so
    /// that DiffPlex's piece indices map 1:1 onto our line lists. When
    /// <see cref="DiffOptions.IgnoreWhitespace"/> is true we additionally
    /// collapse all internal whitespace (matching git's <c>-w</c> /
    /// <c>--ignore-all-space</c>) so DiffPlex's hash treats whitespace-only
    /// differences as equal — DiffPlex's own <c>ignoreWhiteSpace</c> flag
    /// only normalises leading/trailing whitespace.
    /// </summary>
    private static (string Left, string Right) BuildComparisonText(
        IReadOnlyList<LineSplitter.Line> leftLines,
        IReadOnlyList<LineSplitter.Line> rightLines,
        DiffOptions options)
    {
        Func<string, string> normalise = options.IgnoreWhitespace
            ? CollapseWhitespace
            : static s => s;

        // Join with '\n' (no trailing newline) — that produces exactly N lines
        // for N input lines, avoiding DiffPlex's "trailing empty piece" quirk.
        string left = string.Join('\n', leftLines.Select(l => normalise(l.Text)));
        string right = string.Join('\n', rightLines.Select(l => normalise(l.Text)));
        return (left, right);
    }

    private static string CollapseWhitespace(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        // Strip every whitespace character (matches git --ignore-all-space).
        Span<char> buf = s.Length <= 256 ? stackalloc char[s.Length] : new char[s.Length];
        int j = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (!char.IsWhiteSpace(s[i]))
            {
                buf[j++] = s[i];
            }
        }
        return new string(buf[..j]);
    }

    private static T RunWithTimeout<T>(Func<T> work, int timeoutMs, out bool timedOut)
    {
        var task = Task.Run(work);
        if (task.Wait(timeoutMs))
        {
            timedOut = false;
            return task.Result;
        }

        timedOut = true;
        return default!;
    }
}
