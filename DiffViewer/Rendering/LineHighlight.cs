using DiffViewer.Models;

namespace DiffViewer.Rendering;

/// <summary>
/// Per-document-line highlight metadata produced by
/// <see cref="DiffHighlightMap"/>. The renderer uses <see cref="Kind"/>
/// to pick a line background; the colorizer overlays the
/// <see cref="IntraLineSpans"/> as brighter word-level spans.
/// </summary>
public sealed record LineHighlight(
    DiffLineKind Kind,
    IReadOnlyList<IntraLineSpan>? IntraLineSpans);

/// <summary>
/// One word-or-whitespace span inside a line, expressed as a half-open
/// 0-based column range plus the kind from
/// <see cref="DiffViewer.Services.IntraLinePieceKind"/>.
/// </summary>
/// <param name="StartColumn">Inclusive 0-based column offset.</param>
/// <param name="EndColumn">Exclusive 0-based column offset.</param>
public sealed record IntraLineSpan(int StartColumn, int EndColumn, IntraLineSpanKind Kind);

public enum IntraLineSpanKind
{
    Inserted,
    Deleted,
}
