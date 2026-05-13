using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ICSharpCode.AvalonEdit;

namespace DiffViewer.Rendering;

/// <summary>
/// Synchronises vertical and horizontal scroll between two AvalonEdit
/// <see cref="TextEditor"/>s. Uses the inner <see cref="ScrollViewer"/>
/// from each editor's template so callers don't have to subclass.
/// </summary>
/// <remarks>
/// AvalonEdit's <see cref="TextEditor"/> hosts its content in a
/// <c>ScrollViewer</c> named <c>PART_ScrollViewer</c> in the default
/// template; we fish it out via <see cref="System.Windows.FrameworkTemplate"/>
/// after the editors are loaded. A re-entrancy flag suppresses the
/// inevitable echo when the second editor's scroll fires its own
/// <see cref="ScrollViewer.ScrollChanged"/> event in response.
/// </remarks>
public sealed class TextEditorScrollSync : IDisposable
{
    private readonly TextEditor _left;
    private readonly TextEditor _right;
    private ScrollViewer? _leftScroll;
    private ScrollViewer? _rightScroll;
    private bool _suspend;

    public TextEditorScrollSync(TextEditor left, TextEditor right)
    {
        _left = left ?? throw new ArgumentNullException(nameof(left));
        _right = right ?? throw new ArgumentNullException(nameof(right));

        // Editors may not be templated yet; defer.
        _left.Loaded += OnEditorLoaded;
        _right.Loaded += OnEditorLoaded;
        TryAttach();
    }

    private void OnEditorLoaded(object sender, System.Windows.RoutedEventArgs e) => TryAttach();

    private void TryAttach()
    {
        _leftScroll ??= FindScrollViewer(_left);
        _rightScroll ??= FindScrollViewer(_right);

        if (_leftScroll is not null && _rightScroll is not null)
        {
            _leftScroll.ScrollChanged += OnLeftScrollChanged;
            _rightScroll.ScrollChanged += OnRightScrollChanged;
            _left.Loaded -= OnEditorLoaded;
            _right.Loaded -= OnEditorLoaded;
        }
    }

    private static ScrollViewer? FindScrollViewer(TextEditor editor)
    {
        editor.ApplyTemplate();
        return editor.Template?.FindName("PART_ScrollViewer", editor) as ScrollViewer;
    }

    private void OnLeftScrollChanged(object sender, ScrollChangedEventArgs e) =>
        Mirror(e, _rightScroll);

    private void OnRightScrollChanged(object sender, ScrollChangedEventArgs e) =>
        Mirror(e, _leftScroll);

    private void Mirror(ScrollChangedEventArgs e, ScrollViewer? target)
    {
        if (_suspend || target is null) return;
        if (e.VerticalChange == 0 && e.HorizontalChange == 0) return;

        _suspend = true;
        try
        {
            if (e.VerticalChange != 0)
                target.ScrollToVerticalOffset(((ScrollViewer)e.OriginalSource).VerticalOffset);
            if (e.HorizontalChange != 0)
                target.ScrollToHorizontalOffset(((ScrollViewer)e.OriginalSource).HorizontalOffset);
        }
        finally
        {
            _suspend = false;
        }
    }

    public void Dispose()
    {
        if (_leftScroll is not null) _leftScroll.ScrollChanged -= OnLeftScrollChanged;
        if (_rightScroll is not null) _rightScroll.ScrollChanged -= OnRightScrollChanged;
        _left.Loaded -= OnEditorLoaded;
        _right.Loaded -= OnEditorLoaded;
    }
}
