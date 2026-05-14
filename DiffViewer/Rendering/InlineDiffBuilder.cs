using System.Text;
using DiffViewer.Models;

namespace DiffViewer.Rendering;

/// <summary>
/// Builds the document that the inline-mode editor renders: the full
/// right-side file with hunks woven in. Lines are emitted verbatim, with
/// no <c>@@</c> headers and no <c>+</c>/<c>-</c>/space prefix character.
/// Added / removed / modified lines are signalled exclusively by per-line
/// background tints (<see cref="InlineDiffBackgroundRenderer"/>) and
/// intra-line word spans (<see cref="IntraLineColorizer"/>), so columns
/// line up with the underlying file and the inline view matches the
/// side-by-side view's "show the file, mark diffs by color" model.
/// Returns the rendered text plus a per-line <see cref="LineHighlight"/>
/// (kind + optional intra-line spans) for the renderer + colorizer.
/// </summary>
public static class InlineDiffBuilder
{
    /// <summary>
    /// Inline-mode document plus everything callers need to project inline
    /// output lines back onto the source files.
    /// </summary>
    /// <param name="Text">The rendered inline document.</param>
    /// <param name="LineHighlights">
    /// Per-line highlight info (kind + intra-line spans); only present for
    /// non-context lines.
    /// </param>
    /// <param name="LineToSourceLines">
    /// Per-inline-output-line mapping back to the underlying old / new buffers.
    /// Index 0 ↔ inline line 1. Each entry is a <c>(OldLine, NewLine)</c>
    /// pair of 1-based source line numbers; either can be <c>null</c>
    /// (a pure-delete output line has <c>NewLine == null</c>, a pure-insert
    /// has <c>OldLine == null</c>). The viewport indicator uses this to
    /// project the editor's visible window onto the two-column hunk bar.
    /// </param>
    public sealed record InlineDocument(
        string Text,
        IReadOnlyDictionary<int, LineHighlight> LineHighlights,
        IReadOnlyList<(int? OldLine, int? NewLine)> LineToSourceLines);

    private static readonly InlineDocument _empty =
        new(string.Empty,
            new Dictionary<int, LineHighlight>(),
            Array.Empty<(int? OldLine, int? NewLine)>());

    /// <summary>
    /// The empty inline document. Returned when there's no file selected,
    /// a placeholder is showing, or the diff is otherwise unavailable.
    /// </summary>
    public static InlineDocument Empty => _empty;

    /// <summary>
    /// Build an inline document showing the <em>full</em> right-side file with
    /// hunks woven in. Every line — both inside and outside hunks — is
    /// emitted <em>verbatim</em>, with no <c>+</c>/<c>-</c>/space prefix
    /// character. The user sees the file as-is; added / removed / modified
    /// lines are tinted via the inline background renderer (full-line
    /// red / green / yellow) and word-level intra-line spans are overlaid by
    /// the intra-line colorizer. Same channel as side-by-side mode — both
    /// views look like the file with diffs coloured rather than a unified-
    /// diff text dump.
    ///
    /// <para>Removed lines are interleaved into the right-side flow (they're
    /// not in the right blob); the user identifies them by their red tint,
    /// not by a leading <c>-</c>.</para>
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
        // no changes (the user sees the raw file). With no hunks the two
        // sides are byte-identical, so each output line maps to itself on
        // both sides.
        if (hunks.Count == 0)
        {
            var identity = new List<(int? OldLine, int? NewLine)>(rightLines.Count);
            for (int i = 1; i <= rightLines.Count; i++)
            {
                identity.Add((i, i));
            }
            return new InlineDocument(right, new Dictionary<int, LineHighlight>(), identity);
        }

        var sb = new StringBuilder();
        var lineHighlights = new Dictionary<int, LineHighlight>();
        var lineToSourceLines = new List<(int? OldLine, int? NewLine)>();
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
                // Outside hunks the two sides are byte-identical, so a
                // relative offset on the new side maps 1:1 onto the old side.
                lineToSourceLines.Add((oldCursor + (i - newCursor), i));
                currentOutputLine++;
            }

            // Emit the hunk content verbatim — no +/-/space prefix character.
            // Each line keeps the column positions it has in the underlying
            // file, so context lines around the diff align visually with
            // lines emitted from outside the hunk (which are also verbatim).
            // Added / removed / modified lines are signalled to the user
            // exclusively by the InlineDiffBackgroundRenderer's per-line
            // tint and the IntraLineColorizer's word-level spans — i.e. the
            // same channel side-by-side mode uses, keeping the two views
            // visually consistent.
            foreach (var line in hunk.Lines)
            {
                sb.Append(line.Text).Append('\n');
                if (line.Kind != DiffLineKind.Context)
                {
                    lineHighlights[currentOutputLine] = BuildHighlight(line, map);
                }
                // DiffLine already carries the per-side line numbers: both
                // set for Context/Modified, OldLineNumber=null for Inserted,
                // NewLineNumber=null for Deleted. That's exactly the shape
                // the viewport indicator's "nearest non-null" lookup wants.
                lineToSourceLines.Add((line.OldLineNumber, line.NewLineNumber));
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
            lineToSourceLines.Add((oldCursor + (i - newCursor), i));
            currentOutputLine++;
        }

        return new InlineDocument(sb.ToString(), lineHighlights, lineToSourceLines);
    }

    /// <summary>
    /// Look up the intra-line spans for <paramref name="line"/> in <paramref name="map"/>
    /// (keyed by Old/NewLineNumber) and pack them with the line's kind into a
    /// <see cref="LineHighlight"/>. The kind on the returned highlight stays
    /// Deleted / Inserted (not Modified, which is what the map stamps for
    /// paired lines) so the inline background renderer keeps tinting red/green
    /// rather than the side-by-side modified yellow.
    ///
    /// <para>Spans are returned unchanged: <see cref="BuildFullFile"/> emits
    /// each line verbatim with no prefix character, so the colorizer's
    /// <c>lineStart + StartColumn</c> arithmetic lands directly on the
    /// changed characters.</para>
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
        return new LineHighlight(line.Kind, spans);
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
