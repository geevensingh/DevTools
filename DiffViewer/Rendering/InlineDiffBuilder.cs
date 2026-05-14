using System.Text;
using DiffViewer.Models;

namespace DiffViewer.Rendering;

/// <summary>
/// Builds the inline-mode document: a single text buffer where each hunk
/// is preceded by a <c>@@ -OldStart,OldLen +NewStart,NewLen @@</c> header
/// and lines are prefixed with <c> </c> (context), <c>-</c> (deleted) or
/// <c>+</c> (inserted). Returns both the rendered text and a per-line
/// <see cref="LineHighlight"/> (kind + optional intra-line spans) for the
/// renderer + colorizer.
/// </summary>
public static class InlineDiffBuilder
{
    public sealed record InlineDocument(
        string Text,
        IReadOnlyDictionary<int, LineHighlight> LineHighlights);

    private static readonly InlineDocument _empty =
        new(string.Empty, new Dictionary<int, LineHighlight>());

    public static InlineDocument Build(IReadOnlyList<DiffHunk> hunks)
        => Build(hunks, DiffHighlightMap.Empty);

    public static InlineDocument Build(IReadOnlyList<DiffHunk> hunks, DiffHighlightMap map)
    {
        if (hunks.Count == 0)
        {
            return _empty;
        }

        var sb = new StringBuilder();
        var lineHighlights = new Dictionary<int, LineHighlight>();
        int currentLine = 1;

        for (int h = 0; h < hunks.Count; h++)
        {
            var hunk = hunks[h];

            // Hunk header line - rendered without a tint so the renderer
            // skips it (no entry in lineHighlights).
            sb.Append("@@ -")
              .Append(hunk.OldStartLine).Append(',').Append(hunk.OldLineCount)
              .Append(" +")
              .Append(hunk.NewStartLine).Append(',').Append(hunk.NewLineCount)
              .Append(" @@");
            if (hunk.FunctionContext is { Length: > 0 })
            {
                sb.Append(' ').Append(hunk.FunctionContext);
            }
            sb.Append('\n');
            currentLine++;

            foreach (var line in hunk.Lines)
            {
                char prefix = line.Kind switch
                {
                    DiffLineKind.Inserted => '+',
                    DiffLineKind.Deleted => '-',
                    DiffLineKind.Modified => '~', // unused in current builder
                    _ => ' ',
                };

                sb.Append(prefix).Append(line.Text).Append('\n');
                if (line.Kind != DiffLineKind.Context)
                {
                    lineHighlights[currentLine] = BuildHighlight(line, map);
                }
                currentLine++;
            }

            // Blank separator between hunks (skip after the last).
            if (h < hunks.Count - 1)
            {
                sb.Append('\n');
                currentLine++;
            }
        }

        return new InlineDocument(sb.ToString(), lineHighlights);
    }

    /// <summary>
    /// Build an inline document showing the <em>full</em> right-side file with
    /// hunks woven in. Unchanged regions outside hunks are emitted verbatim
    /// (no @@ header, no prefix); inside hunks each line gets the usual
    /// <c>+</c> / <c>-</c> / <c>(space)</c> prefix and added/removed lines
    /// pick up a <see cref="LineHighlight"/> entry for the renderer to tint
    /// and the colorizer to overlay intra-line spans.
    ///
    /// <para><paramref name="map"/> supplies the per-line intra-line spans
    /// computed by <see cref="DiffHighlightMap.FromHunks"/>; pass
    /// <see cref="DiffHighlightMap.Empty"/> for tests that don't care about
    /// spans (lines will still get a <see cref="LineHighlight"/> with the
    /// correct kind, just no spans).</para>
    ///
    /// <para>Used by <see cref="ViewModels.DiffPaneViewModel"/> in inline mode so the
    /// user sees the whole file with diffs highlighted, not just the
    /// 3-line-context hunks. Side-by-side mode is unaffected — it already
    /// shows the full blobs in two editors.</para>
    /// </summary>
    public static InlineDocument BuildFullFile(
        string left, string right, IReadOnlyList<DiffHunk> hunks, DiffHighlightMap map)
    {
        var leftLines = SplitLines(left);
        var rightLines = SplitLines(right);

        // No diff at all: emit the right-side blob verbatim, no prefixes,
        // no highlights. Matches the screenshot expectation when there are
        // no changes (the user sees the raw file).
        if (hunks.Count == 0)
        {
            return new InlineDocument(right, new Dictionary<int, LineHighlight>());
        }

        var sb = new StringBuilder();
        var lineHighlights = new Dictionary<int, LineHighlight>();
        int currentOutputLine = 1;
        int oldCursor = 1; // 1-based next-unread line of left file
        int newCursor = 1; // 1-based next-unread line of right file

        for (int h = 0; h < hunks.Count; h++)
        {
            var hunk = hunks[h];

            // Emit unchanged context lines BEFORE this hunk by walking the
            // right (new) file from newCursor up to (but not including)
            // hunk.NewStartLine. Use the right side as the source of truth
            // for context — outside hunks the two sides are byte-identical.
            int hunkNewStart = hunk.NewStartLine > 0 ? hunk.NewStartLine : newCursor;
            for (int i = newCursor; i < hunkNewStart && i <= rightLines.Count; i++)
            {
                sb.Append(rightLines[i - 1]).Append('\n');
                currentOutputLine++;
            }

            // Emit the hunk content with prefixes; each non-context line gets
            // a LineHighlight built from the map's intra-line spans.
            foreach (var line in hunk.Lines)
            {
                char prefix = line.Kind switch
                {
                    DiffLineKind.Inserted => '+',
                    DiffLineKind.Deleted => '-',
                    DiffLineKind.Modified => '~',
                    _ => ' ',
                };
                sb.Append(prefix).Append(line.Text).Append('\n');
                if (line.Kind != DiffLineKind.Context)
                {
                    lineHighlights[currentOutputLine] = BuildHighlight(line, map);
                }
                currentOutputLine++;
            }

            // Advance cursors past the consumed regions on each side.
            oldCursor = (hunk.OldStartLine > 0 ? hunk.OldStartLine : oldCursor) + hunk.OldLineCount;
            newCursor = hunkNewStart + hunk.NewLineCount;
        }

        // Tail: emit any remaining unchanged lines after the last hunk.
        for (int i = newCursor; i <= rightLines.Count; i++)
        {
            sb.Append(rightLines[i - 1]).Append('\n');
            currentOutputLine++;
        }

        return new InlineDocument(sb.ToString(), lineHighlights);
    }

    /// <summary>
    /// Look up the intra-line spans for <paramref name="line"/> in <paramref name="map"/>
    /// (keyed by Old/NewLineNumber) and pack them with the line's kind into a
    /// <see cref="LineHighlight"/>. The kind on the returned highlight stays
    /// Deleted / Inserted (not Modified, which is what the map stamps for
    /// paired lines) so the inline background renderer keeps tinting red/green
    /// rather than the side-by-side modified yellow.
    ///
    /// <para>Intra-line span columns are computed against the original line
    /// text; the inline builder prepends a single <c>+</c> / <c>-</c> /
    /// <c>(space)</c> prefix to every line, so the displayed line is one
    /// character wider. Shift each span by +1 so the colorizer's
    /// <c>lineStart + StartColumn</c> arithmetic lands on the right
    /// character.</para>
    /// </summary>
    private static LineHighlight BuildHighlight(DiffLine line, DiffHighlightMap map)
    {
        IReadOnlyList<IntraLineSpan>? spans = null;
        switch (line.Kind)
        {
            case DiffLineKind.Deleted:
                if (line.OldLineNumber is int oldLn &&
                    map.LeftLines.TryGetValue(oldLn, out var leftHl))
                {
                    spans = leftHl.IntraLineSpans;
                }
                break;
            case DiffLineKind.Inserted:
                if (line.NewLineNumber is int newLn &&
                    map.RightLines.TryGetValue(newLn, out var rightHl))
                {
                    spans = rightHl.IntraLineSpans;
                }
                break;
        }
        return new LineHighlight(line.Kind, ShiftSpansForPrefix(spans));
    }

    /// <summary>
    /// Shift every span by +1 column to account for the 1-char prefix the
    /// inline builder prepends to every line.
    /// </summary>
    private static IReadOnlyList<IntraLineSpan>? ShiftSpansForPrefix(IReadOnlyList<IntraLineSpan>? spans)
    {
        if (spans is null || spans.Count == 0) return spans;
        var shifted = new IntraLineSpan[spans.Count];
        for (int i = 0; i < spans.Count; i++)
        {
            var s = spans[i];
            shifted[i] = new IntraLineSpan(s.StartColumn + 1, s.EndColumn + 1, s.Kind);
        }
        return shifted;
    }

    private static List<string> SplitLines(string text)
    {
        // Preserve mixed-EOL inputs by splitting on the canonical break and
        // stripping any trailing CR. We don't preserve original line endings
        // here — the inline view emits LF — because AvalonEdit normalises
        // anyway and the diff highlighting works off line numbers.
        if (text.Length == 0) return new List<string>();
        var raw = text.Split('\n');
        var result = new List<string>(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            var s = raw[i];
            if (s.Length > 0 && s[^1] == '\r') s = s[..^1];
            // Drop the synthetic empty trailing element produced by a final '\n'.
            if (i == raw.Length - 1 && s.Length == 0) break;
            result.Add(s);
        }
        return result;
    }
}
