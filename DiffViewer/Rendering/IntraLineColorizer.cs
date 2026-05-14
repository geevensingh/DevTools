using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace DiffViewer.Rendering;

/// <summary>
/// Overlays brighter word-level tints inside Modified lines using the
/// span ranges produced by <see cref="DiffHighlightMap"/>. One instance
/// per editor; reads from <see cref="LineHighlights"/> on every line
/// colorize, so updating the map + calling <c>TextView.Redraw()</c>
/// repaints the spans.
/// </summary>
public sealed class IntraLineColorizer : DocumentColorizingTransformer
{
    private readonly DiffSide _side;
    private readonly DiffColorScheme _scheme;

    public IReadOnlyDictionary<int, LineHighlight>? LineHighlights { get; set; }

    public IntraLineColorizer(DiffSide side, DiffColorScheme scheme)
    {
        _side = side;
        _scheme = scheme ?? throw new ArgumentNullException(nameof(scheme));
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (LineHighlights is null) return;
        if (!LineHighlights.TryGetValue(line.LineNumber, out var highlight)) return;
        if (highlight.IntraLineSpans is null || highlight.IntraLineSpans.Count == 0) return;

        int lineStart = line.Offset;
        int lineEnd = line.EndOffset;

        foreach (var span in highlight.IntraLineSpans)
        {
            int start = lineStart + span.StartColumn;
            int end = lineStart + span.EndColumn;
            if (start >= lineEnd) continue;
            if (end > lineEnd) end = lineEnd;
            if (start >= end) continue;

            var brush = PickBrush(span.Kind);
            ChangeLinePart(start, end, element =>
            {
                element.BackgroundBrush = brush;
            });
        }
    }

    /// <summary>
    /// Pick the brighter intra-line brush. Side-by-side editors fix the
    /// brush by their side (left = removed, right = added) — every span on
    /// that side is the same kind anyway. The inline editor renders both
    /// kinds in one column, so it picks per-span from <paramref name="spanKind"/>.
    /// </summary>
    private System.Windows.Media.Brush PickBrush(IntraLineSpanKind spanKind) => _side switch
    {
        DiffSide.Left => _scheme.RemovedIntraLineBackground,
        DiffSide.Right => _scheme.AddedIntraLineBackground,
        DiffSide.Inline => spanKind == IntraLineSpanKind.Deleted
            ? _scheme.RemovedIntraLineBackground
            : _scheme.AddedIntraLineBackground,
        _ => _scheme.AddedIntraLineBackground,
    };
}
