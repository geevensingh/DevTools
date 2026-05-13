using System.ComponentModel;
using System.Windows.Controls;
using DiffViewer.Rendering;
using DiffViewer.ViewModels;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;

namespace DiffViewer.Views;

/// <summary>
/// Code-behind for <see cref="DiffPaneView"/>. Wires up:
/// <list type="bullet">
/// <item>The <see cref="TextEditorScrollSync"/> between the two side-by-side editors.</item>
/// <item>Per-side <see cref="DiffBackgroundRenderer"/> + <see cref="IntraLineColorizer"/>.</item>
/// <item>An <see cref="InlineDiffBackgroundRenderer"/> on the inline editor.</item>
/// <item>The <see cref="DiffPaneViewModel.ShowVisibleWhitespace"/> bridge to AvalonEdit's
/// <c>TextEditorOptions.ShowSpaces</c> / <c>ShowTabs</c> / <c>ShowEndOfLine</c>.</item>
/// <item>The <see cref="DiffPaneViewModel.HunkNavigationRequested"/> handler that scrolls
/// the active editors to the requested 1-based line numbers.</item>
/// </list>
/// </summary>
public partial class DiffPaneView : UserControl
{
    private TextEditorScrollSync? _scrollSync;
    private DiffBackgroundRenderer? _leftBg;
    private DiffBackgroundRenderer? _rightBg;
    private IntraLineColorizer? _leftIntra;
    private IntraLineColorizer? _rightIntra;
    private InlineDiffBackgroundRenderer? _inlineBg;
    private DiffPaneViewModel? _vm;

    public DiffPaneView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _scrollSync ??= new TextEditorScrollSync(LeftEditor, RightEditor);

        var scheme = DiffColorScheme.Classic;

        if (_leftBg is null)
        {
            _leftBg = new DiffBackgroundRenderer(DiffSide.Left, scheme);
            LeftEditor.TextArea.TextView.BackgroundRenderers.Add(_leftBg);
        }
        if (_rightBg is null)
        {
            _rightBg = new DiffBackgroundRenderer(DiffSide.Right, scheme);
            RightEditor.TextArea.TextView.BackgroundRenderers.Add(_rightBg);
        }
        if (_leftIntra is null)
        {
            _leftIntra = new IntraLineColorizer(DiffSide.Left, scheme);
            LeftEditor.TextArea.TextView.LineTransformers.Add(_leftIntra);
        }
        if (_rightIntra is null)
        {
            _rightIntra = new IntraLineColorizer(DiffSide.Right, scheme);
            RightEditor.TextArea.TextView.LineTransformers.Add(_rightIntra);
        }
        if (_inlineBg is null)
        {
            _inlineBg = new InlineDiffBackgroundRenderer(scheme);
            InlineEditor.TextArea.TextView.BackgroundRenderers.Add(_inlineBg);
        }

        AttachToViewModel(DataContext as DiffPaneViewModel);
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _scrollSync?.Dispose();
        _scrollSync = null;

        DetachFromViewModel();

        RemoveRenderer(LeftEditor, ref _leftBg);
        RemoveRenderer(RightEditor, ref _rightBg);
        RemoveColorizer(LeftEditor, ref _leftIntra);
        RemoveColorizer(RightEditor, ref _rightIntra);
        RemoveRenderer(InlineEditor, ref _inlineBg);
    }

    private static void RemoveRenderer<T>(TextEditor editor, ref T? renderer)
        where T : class, ICSharpCode.AvalonEdit.Rendering.IBackgroundRenderer
    {
        if (renderer is null) return;
        editor.TextArea.TextView.BackgroundRenderers.Remove(renderer);
        renderer = null;
    }

    private static void RemoveColorizer<T>(TextEditor editor, ref T? colorizer)
        where T : class, ICSharpCode.AvalonEdit.Rendering.IVisualLineTransformer
    {
        if (colorizer is null) return;
        editor.TextArea.TextView.LineTransformers.Remove(colorizer);
        colorizer = null;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        DetachFromViewModel();
        AttachToViewModel(e.NewValue as DiffPaneViewModel);
    }

    private void AttachToViewModel(DiffPaneViewModel? vm)
    {
        _vm = vm;
        if (_vm is null) return;
        _vm.HighlightMapChanged += OnHighlightMapChanged;
        _vm.HunkNavigationRequested += OnHunkNavigationRequested;
        _vm.PropertyChanged += OnVmPropertyChanged;

        ApplyHighlightMap();
        ApplyVisibleWhitespace();
    }

    private void DetachFromViewModel()
    {
        if (_vm is null) return;
        _vm.HighlightMapChanged -= OnHighlightMapChanged;
        _vm.HunkNavigationRequested -= OnHunkNavigationRequested;
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = null;
    }

    private void OnHighlightMapChanged(object? sender, EventArgs e) => ApplyHighlightMap();

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiffPaneViewModel.ShowVisibleWhitespace))
        {
            ApplyVisibleWhitespace();
        }
    }

    private void ApplyHighlightMap()
    {
        if (_vm is null) return;
        var map = _vm.HighlightMap;

        if (_leftBg is not null) _leftBg.LineHighlights = map.LeftLines;
        if (_rightBg is not null) _rightBg.LineHighlights = map.RightLines;
        if (_leftIntra is not null) _leftIntra.LineHighlights = map.LeftLines;
        if (_rightIntra is not null) _rightIntra.LineHighlights = map.RightLines;
        if (_inlineBg is not null) _inlineBg.LineKinds = _vm.InlineLineKinds;

        LeftEditor.TextArea.TextView.Redraw();
        RightEditor.TextArea.TextView.Redraw();
        InlineEditor.TextArea.TextView.Redraw();
    }

    private void ApplyVisibleWhitespace()
    {
        if (_vm is null) return;
        bool show = _vm.ShowVisibleWhitespace;
        SetWhitespace(LeftEditor, show);
        SetWhitespace(RightEditor, show);
        SetWhitespace(InlineEditor, show);
    }

    private static void SetWhitespace(TextEditor editor, bool show)
    {
        editor.Options.ShowSpaces = show;
        editor.Options.ShowTabs = show;
        editor.Options.ShowEndOfLine = show;
    }

    private void OnHunkNavigationRequested(object? sender, HunkNavigationEventArgs e)
    {
        // Scroll all three editors to the relevant line; whichever one is
        // currently visible is the one the user sees jump.
        ScrollEditorToLine(LeftEditor, e.LeftLine);
        ScrollEditorToLine(RightEditor, e.RightLine);
        // Inline mode: the line numbers above are blob-relative, but the
        // inline editor's own line numbering is hunk-prefix-based. The
        // simplest approximation: scroll to the new-side line + 1 (header).
        ScrollEditorToLine(InlineEditor, e.RightLine);
    }

    private static void ScrollEditorToLine(TextEditor editor, int line)
    {
        if (line < 1) return;
        if (editor.Document is null) return;
        if (line > editor.Document.LineCount) line = editor.Document.LineCount;
        if (line < 1) return;
        DocumentLine docLine = editor.Document.GetLineByNumber(line);
        editor.ScrollToLine(line);
        editor.CaretOffset = docLine.Offset;
    }
}
