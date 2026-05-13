using System.Windows;
using System.Windows.Media;
using DiffViewer.Models;
using ICSharpCode.AvalonEdit.Rendering;

namespace DiffViewer.Rendering;

/// <summary>
/// Side of a diff a renderer / colorizer applies to. The viewmodel
/// produces one <see cref="DiffHighlightMap"/> covering both sides; each
/// editor's renderer reads only its corresponding dictionary.
/// </summary>
public enum DiffSide
{
    Left,
    Right,
}

/// <summary>
/// Paints per-line background tints for added / removed / modified lines.
/// One instance per <see cref="ICSharpCode.AvalonEdit.TextEditor"/>; reads
/// its highlight dictionary from <see cref="LineHighlights"/> on every
/// <see cref="Draw"/>, so updating the map plus calling
/// <c>TextView.Redraw()</c> is enough to repaint.
/// </summary>
public sealed class DiffBackgroundRenderer : IBackgroundRenderer
{
    private readonly DiffSide _side;
    private readonly DiffColorScheme _scheme;

    public IReadOnlyDictionary<int, LineHighlight>? LineHighlights { get; set; }

    public DiffBackgroundRenderer(DiffSide side, DiffColorScheme scheme)
    {
        _side = side;
        _scheme = scheme ?? throw new ArgumentNullException(nameof(scheme));
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (LineHighlights is null || LineHighlights.Count == 0) return;
        if (textView.VisualLinesValid == false) return;

        foreach (var visualLine in textView.VisualLines)
        {
            int docLine = visualLine.FirstDocumentLine.LineNumber;
            if (!LineHighlights.TryGetValue(docLine, out var highlight)) continue;

            Brush brush = PickBrush(highlight.Kind);

            // Paint the full line width so the tint extends to the right edge
            // even on short lines; matches GitHub / VS Code line-diff style.
            foreach (var rect in BackgroundGeometryBuilder.GetRectsFromVisualSegment(
                textView, visualLine, 0, int.MaxValue))
            {
                var fullWidth = new Rect(
                    rect.X,
                    rect.Y,
                    Math.Max(rect.Width, textView.ActualWidth - rect.X),
                    rect.Height);
                drawingContext.DrawRectangle(brush, null, fullWidth);
            }
        }
    }

    private Brush PickBrush(DiffLineKind kind) => (_side, kind) switch
    {
        (DiffSide.Left, DiffLineKind.Deleted) => _scheme.RemovedLineBackground,
        (DiffSide.Left, DiffLineKind.Modified) => _scheme.ModifiedLineBackground,
        (DiffSide.Right, DiffLineKind.Inserted) => _scheme.AddedLineBackground,
        (DiffSide.Right, DiffLineKind.Modified) => _scheme.ModifiedLineBackground,
        _ => Brushes.Transparent,
    };
}
