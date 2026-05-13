using DiffViewer.Models;
using DiffViewer.Services;

namespace DiffViewer.Rendering;

/// <summary>
/// Per-side mapping from 1-based document line number to
/// <see cref="LineHighlight"/>. Built once per diff computation and handed
/// to the background renderer + intra-line colorizer attached to each
/// AvalonEdit <c>TextEditor</c>.
/// </summary>
public sealed class DiffHighlightMap
{
    /// <summary>Line highlights for the left (old) document.</summary>
    public IReadOnlyDictionary<int, LineHighlight> LeftLines { get; }

    /// <summary>Line highlights for the right (new) document.</summary>
    public IReadOnlyDictionary<int, LineHighlight> RightLines { get; }

    public static DiffHighlightMap Empty { get; } = new(
        new Dictionary<int, LineHighlight>(),
        new Dictionary<int, LineHighlight>());

    public DiffHighlightMap(
        IReadOnlyDictionary<int, LineHighlight> leftLines,
        IReadOnlyDictionary<int, LineHighlight> rightLines)
    {
        LeftLines = leftLines;
        RightLines = rightLines;
    }

    /// <summary>
    /// Walk a hunk model and produce highlight maps for both sides. When
    /// <paramref name="diffService"/> is non-null and
    /// <paramref name="enableIntraLine"/> is true, blocks with both deletes
    /// and inserts get word-level spans paired up <c>min(D, I)</c> lines
    /// at a time.
    /// </summary>
    public static DiffHighlightMap FromHunks(
        IReadOnlyList<DiffHunk> hunks,
        IDiffService? diffService,
        bool enableIntraLine,
        bool ignoreWhitespace)
    {
        if (hunks.Count == 0) return Empty;

        var left = new Dictionary<int, LineHighlight>();
        var right = new Dictionary<int, LineHighlight>();

        foreach (var hunk in hunks)
        {
            // Walk the hunk's lines splitting them into adjacent
            // delete-then-insert blocks so we can pair them for intra-line.
            int i = 0;
            while (i < hunk.Lines.Count)
            {
                var line = hunk.Lines[i];
                if (line.Kind == DiffLineKind.Context)
                {
                    i++;
                    continue;
                }

                int blockStart = i;
                while (i < hunk.Lines.Count && hunk.Lines[i].Kind == DiffLineKind.Deleted) i++;
                int deletedEnd = i;
                while (i < hunk.Lines.Count && hunk.Lines[i].Kind == DiffLineKind.Inserted) i++;
                int insertedEnd = i;

                int deletedCount = deletedEnd - blockStart;
                int insertedCount = insertedEnd - deletedEnd;
                int paired = Math.Min(deletedCount, insertedCount);

                // Paired lines: emit Modified with intra-line spans.
                for (int k = 0; k < paired; k++)
                {
                    var del = hunk.Lines[blockStart + k];
                    var ins = hunk.Lines[deletedEnd + k];

                    IReadOnlyList<IntraLineSpan>? leftSpans = null;
                    IReadOnlyList<IntraLineSpan>? rightSpans = null;
                    if (enableIntraLine && diffService is not null)
                    {
                        (leftSpans, rightSpans) = ComputeIntraLineSpans(
                            diffService, del.Text, ins.Text, ignoreWhitespace);
                    }

                    if (del.OldLineNumber is int oldLn)
                    {
                        left[oldLn] = new LineHighlight(DiffLineKind.Modified, leftSpans);
                    }
                    if (ins.NewLineNumber is int newLn)
                    {
                        right[newLn] = new LineHighlight(DiffLineKind.Modified, rightSpans);
                    }
                }

                // Unpaired deletes (extra removed lines).
                for (int k = paired; k < deletedCount; k++)
                {
                    var del = hunk.Lines[blockStart + k];
                    if (del.OldLineNumber is int oldLn)
                    {
                        left[oldLn] = new LineHighlight(DiffLineKind.Deleted, null);
                    }
                }

                // Unpaired inserts (extra added lines).
                for (int k = paired; k < insertedCount; k++)
                {
                    var ins = hunk.Lines[deletedEnd + k];
                    if (ins.NewLineNumber is int newLn)
                    {
                        right[newLn] = new LineHighlight(DiffLineKind.Inserted, null);
                    }
                }
            }
        }

        return new DiffHighlightMap(left, right);
    }

    /// <summary>
    /// Run the intra-line word diff and bucket the resulting pieces into
    /// (oldSpans, newSpans) by walking the old and new lines independently.
    /// </summary>
    private static (IReadOnlyList<IntraLineSpan> Left, IReadOnlyList<IntraLineSpan> Right)
        ComputeIntraLineSpans(IDiffService diffService, string oldLine, string newLine, bool ignoreWhitespace)
    {
        var pieces = diffService.ComputeIntraLineDiff(oldLine, newLine, ignoreWhitespace);

        var leftSpans = new List<IntraLineSpan>();
        var rightSpans = new List<IntraLineSpan>();

        int leftCol = 0;
        int rightCol = 0;

        foreach (var piece in pieces)
        {
            int len = piece.Text.Length;
            switch (piece.Kind)
            {
                case IntraLinePieceKind.Unchanged:
                    leftCol += len;
                    rightCol += len;
                    break;
                case IntraLinePieceKind.Deleted:
                    if (len > 0)
                    {
                        leftSpans.Add(new IntraLineSpan(leftCol, leftCol + len, IntraLineSpanKind.Deleted));
                    }
                    leftCol += len;
                    break;
                case IntraLinePieceKind.Inserted:
                    if (len > 0)
                    {
                        rightSpans.Add(new IntraLineSpan(rightCol, rightCol + len, IntraLineSpanKind.Inserted));
                    }
                    rightCol += len;
                    break;
            }
        }

        return (leftSpans, rightSpans);
    }
}
