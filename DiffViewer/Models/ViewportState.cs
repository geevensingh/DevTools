namespace DiffViewer.Models;

/// <summary>
/// Current editor viewport projected onto each side's source line
/// numbers. In side-by-side mode the two ranges come directly from the
/// left and right editors. In inline mode both ranges come from the
/// inline editor's visible window, projected through the
/// <c>InlineLineToSourceLines</c> map.
///
/// <para>A <c>0</c> on either pair of fields means "this side is
/// unrepresented in the visible window" (only happens in inline mode
/// when the window contains only the other side's lines). The renderer
/// turns that into a single-column band (no trapezoid).</para>
/// </summary>
public sealed record ViewportState(
    int LeftFirstLine,
    int LeftLastLine,
    int RightFirstLine,
    int RightLastLine);
