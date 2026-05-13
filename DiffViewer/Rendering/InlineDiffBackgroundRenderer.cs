using System.Windows;
using System.Windows.Media;
using DiffViewer.Models;
using ICSharpCode.AvalonEdit.Rendering;

namespace DiffViewer.Rendering;

/// <summary>
/// Background renderer used by the inline-mode editor. Reads its
/// per-line kind dictionary from <see cref="LineKinds"/> on every
/// <see cref="Draw"/> and paints the appropriate brush for that kind.
/// Hunk-header lines and blank separators are absent from the dictionary
/// and rendered without a tint.
/// </summary>
public sealed class InlineDiffBackgroundRenderer : IBackgroundRenderer
{
    private readonly DiffColorScheme _scheme;

    public IReadOnlyDictionary<int, DiffLineKind>? LineKinds { get; set; }

    public InlineDiffBackgroundRenderer(DiffColorScheme scheme)
    {
        _scheme = scheme ?? throw new ArgumentNullException(nameof(scheme));
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (LineKinds is null || LineKinds.Count == 0) return;
        if (!textView.VisualLinesValid) return;

        foreach (var visualLine in textView.VisualLines)
        {
            int docLine = visualLine.FirstDocumentLine.LineNumber;
            if (!LineKinds.TryGetValue(docLine, out var kind)) continue;

            Brush brush = kind switch
            {
                DiffLineKind.Inserted => _scheme.AddedLineBackground,
                DiffLineKind.Deleted => _scheme.RemovedLineBackground,
                DiffLineKind.Modified => _scheme.ModifiedLineBackground,
                _ => Brushes.Transparent,
            };

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
}
