using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ICSharpCode.AvalonEdit;

namespace DiffViewer.Rendering;

/// <summary>
/// Drives a pair of standalone WPF <see cref="ScrollBar"/>s from the inner
/// <see cref="ScrollViewer"/>s of two side-by-side AvalonEdit
/// <see cref="TextEditor"/>s, and vice-versa. This is the second half of
/// the scroll-sync story: <see cref="TextEditorScrollSync"/> keeps the two
/// editors' offsets in lock-step, and this class projects that shared
/// offset onto a single visible scrollbar for each axis.
///
/// <para>Implementation notes:</para>
/// <list type="bullet">
///   <item>The bars take the <em>maximum</em> of the two editors' scrollable
///   extent / viewport so the user can scroll all the way to the bottom of
///   the longer file (the shorter editor just clamps at its own end).</item>
///   <item>Bar drags / arrow / track clicks fire <see cref="ScrollBar.Scroll"/>;
///   we apply the new offset to both editors. Programmatic <c>Value</c>
///   changes (e.g. mirroring an editor scroll back into the bar) do NOT
///   fire <see cref="ScrollBar.Scroll"/>, so this side of the loop closes
///   without recursion.</item>
///   <item>A re-entrancy guard suppresses the inevitable echo when setting
///   the bar from an editor that we just scrolled in response to the bar.</item>
///   <item>The bars auto-hide when there's nothing to scroll
///   (<see cref="ScrollViewer.ScrollableHeight"/> == 0), matching the
///   <see cref="ScrollBarVisibility.Auto"/> behaviour the editors had
///   before they were unified.</item>
/// </list>
/// </summary>
public sealed class UnifiedScrollBarController : IDisposable
{
    private readonly TextEditor _left;
    private readonly TextEditor _right;
    private readonly ScrollBar _vBar;
    private readonly ScrollBar _hBar;
    private ScrollViewer? _leftSv;
    private ScrollViewer? _rightSv;
    private bool _suspend;

    public UnifiedScrollBarController(TextEditor left, TextEditor right, ScrollBar vBar, ScrollBar hBar)
    {
        _left = left ?? throw new ArgumentNullException(nameof(left));
        _right = right ?? throw new ArgumentNullException(nameof(right));
        _vBar = vBar ?? throw new ArgumentNullException(nameof(vBar));
        _hBar = hBar ?? throw new ArgumentNullException(nameof(hBar));

        _vBar.Orientation = Orientation.Vertical;
        _hBar.Orientation = Orientation.Horizontal;
        _vBar.Minimum = 0;
        _hBar.Minimum = 0;
        // Start hidden — UpdateBars promotes to Visible once we know the
        // extent. Avoids a flash of empty scrollbar during initial load.
        _vBar.Visibility = Visibility.Collapsed;
        _hBar.Visibility = Visibility.Collapsed;

        _vBar.Scroll += OnVBarScroll;
        _hBar.Scroll += OnHBarScroll;

        _left.Loaded += OnEditorLoaded;
        _right.Loaded += OnEditorLoaded;
        TryAttach();
    }

    private void OnEditorLoaded(object sender, RoutedEventArgs e) => TryAttach();

    private void TryAttach()
    {
        _leftSv ??= FindScrollViewer(_left);
        _rightSv ??= FindScrollViewer(_right);

        if (_leftSv is not null && _rightSv is not null)
        {
            _leftSv.ScrollChanged += OnEditorScrollChanged;
            _rightSv.ScrollChanged += OnEditorScrollChanged;
            _left.Loaded -= OnEditorLoaded;
            _right.Loaded -= OnEditorLoaded;
            UpdateBars();
        }
    }

    private static ScrollViewer? FindScrollViewer(TextEditor editor)
    {
        editor.ApplyTemplate();
        return editor.Template?.FindName("PART_ScrollViewer", editor) as ScrollViewer;
    }

    /// <summary>
    /// User dragged / clicked the vertical bar. Push the new offset onto
    /// both editors; the editor-to-editor sync class will clamp each side
    /// to its own scrollable height.
    /// </summary>
    private void OnVBarScroll(object sender, ScrollEventArgs e)
    {
        if (_suspend) return;
        _suspend = true;
        try
        {
            _leftSv?.ScrollToVerticalOffset(e.NewValue);
            _rightSv?.ScrollToVerticalOffset(e.NewValue);
        }
        finally { _suspend = false; }
    }

    private void OnHBarScroll(object sender, ScrollEventArgs e)
    {
        if (_suspend) return;
        _suspend = true;
        try
        {
            _leftSv?.ScrollToHorizontalOffset(e.NewValue);
            _rightSv?.ScrollToHorizontalOffset(e.NewValue);
        }
        finally { _suspend = false; }
    }

    /// <summary>
    /// Either editor scrolled (mouse wheel, keyboard, programmatic). Refresh
    /// the bars' Maximum / ViewportSize / Value to reflect the new state.
    /// Programmatic <see cref="ScrollBar.Value"/> changes don't fire
    /// <see cref="ScrollBar.Scroll"/>, so this never loops back through us.
    /// </summary>
    private void OnEditorScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_suspend) return;
        UpdateBars();
    }

    private void UpdateBars()
    {
        if (_leftSv is null || _rightSv is null) return;

        // Vertical
        double maxV = Math.Max(_leftSv.ScrollableHeight, _rightSv.ScrollableHeight);
        double viewportV = Math.Max(_leftSv.ViewportHeight, _rightSv.ViewportHeight);
        _vBar.Maximum = maxV;
        _vBar.ViewportSize = viewportV;
        _vBar.LargeChange = Math.Max(viewportV, 1);
        _vBar.SmallChange = Math.Max(viewportV / 16, 1);
        _vBar.Value = Math.Max(_leftSv.VerticalOffset, _rightSv.VerticalOffset);
        _vBar.Visibility = maxV > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Horizontal
        double maxH = Math.Max(_leftSv.ScrollableWidth, _rightSv.ScrollableWidth);
        double viewportH = Math.Max(_leftSv.ViewportWidth, _rightSv.ViewportWidth);
        _hBar.Maximum = maxH;
        _hBar.ViewportSize = viewportH;
        _hBar.LargeChange = Math.Max(viewportH, 1);
        _hBar.SmallChange = Math.Max(viewportH / 16, 1);
        _hBar.Value = Math.Max(_leftSv.HorizontalOffset, _rightSv.HorizontalOffset);
        _hBar.Visibility = maxH > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Dispose()
    {
        _vBar.Scroll -= OnVBarScroll;
        _hBar.Scroll -= OnHBarScroll;
        if (_leftSv is not null) _leftSv.ScrollChanged -= OnEditorScrollChanged;
        if (_rightSv is not null) _rightSv.ScrollChanged -= OnEditorScrollChanged;
        _left.Loaded -= OnEditorLoaded;
        _right.Loaded -= OnEditorLoaded;
    }
}
