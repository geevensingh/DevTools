using System.ComponentModel;
using System.IO;
using System.Windows.Controls;
using DiffViewer.Rendering;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;

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
    private UnifiedScrollBarController? _unifiedScrollBars;
    private DiffBackgroundRenderer? _leftBg;
    private DiffBackgroundRenderer? _rightBg;
    private IntraLineColorizer? _leftIntra;
    private IntraLineColorizer? _rightIntra;
    private IntraLineColorizer? _inlineIntra;
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
        _unifiedScrollBars ??= new UnifiedScrollBarController(
            LeftEditor, RightEditor, SideBySideVScroll, SideBySideHScroll);

        var scheme = (DataContext as DiffPaneViewModel)?.CurrentColorScheme ?? DiffColorScheme.Classic;
        InstallRenderers(scheme);

        AttachToViewModel(DataContext as DiffPaneViewModel);
    }

    private void InstallRenderers(DiffColorScheme scheme)
    {
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
        if (_inlineIntra is null)
        {
            // DiffSide.Inline = pick brush per-span by span.Kind (the inline
            // editor renders deleted and inserted lines in one column).
            _inlineIntra = new IntraLineColorizer(DiffSide.Inline, scheme);
            InlineEditor.TextArea.TextView.LineTransformers.Add(_inlineIntra);
        }
    }

    /// <summary>
    /// Tear down all renderers and rebuild them with <paramref name="scheme"/>.
    /// Used when the user picks a new color-scheme preset in the Settings
    /// dialog - <see cref="DiffPaneViewModel.ColorSchemeChanged"/> fires and
    /// the view rebuilds the renderers with the new palette, then re-applies
    /// the current highlight map so the redraw picks up the new colors.
    /// </summary>
    private void ReinstallRenderers(DiffColorScheme scheme)
    {
        RemoveRenderer(LeftEditor, ref _leftBg);
        RemoveRenderer(RightEditor, ref _rightBg);
        RemoveColorizer(LeftEditor, ref _leftIntra);
        RemoveColorizer(RightEditor, ref _rightIntra);
        RemoveRenderer(InlineEditor, ref _inlineBg);
        RemoveColorizer(InlineEditor, ref _inlineIntra);
        InstallRenderers(scheme);
        ApplyHighlightMap();
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _scrollSync?.Dispose();
        _scrollSync = null;
        _unifiedScrollBars?.Dispose();
        _unifiedScrollBars = null;

        DetachFromViewModel();

        RemoveRenderer(LeftEditor, ref _leftBg);
        RemoveRenderer(RightEditor, ref _rightBg);
        RemoveColorizer(LeftEditor, ref _leftIntra);
        RemoveColorizer(RightEditor, ref _rightIntra);
        RemoveRenderer(InlineEditor, ref _inlineBg);
        RemoveColorizer(InlineEditor, ref _inlineIntra);
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
        _vm.ColorSchemeChanged += OnColorSchemeChanged;
        _vm.PropertyChanged += OnVmPropertyChanged;

        ApplyHighlightMap();
        ApplyVisibleWhitespace();
        ApplyTabWidth();
    }

    private void DetachFromViewModel()
    {
        if (_vm is null) return;
        _vm.HighlightMapChanged -= OnHighlightMapChanged;
        _vm.HunkNavigationRequested -= OnHunkNavigationRequested;
        _vm.ColorSchemeChanged -= OnColorSchemeChanged;
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = null;
    }

    private void OnColorSchemeChanged(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        ReinstallRenderers(_vm.CurrentColorScheme);
    }

    private void OnHighlightMapChanged(object? sender, EventArgs e)
    {
        // New document content → drop the horizontal high-water mark so
        // the bar's thumb adapts to this file's line widths instead of
        // remembering the previous file's.
        _unifiedScrollBars?.ResetHighWaterMark();
        ApplyHighlightMap();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiffPaneViewModel.ShowVisibleWhitespace))
        {
            ApplyVisibleWhitespace();
        }
        else if (e.PropertyName == nameof(DiffPaneViewModel.TabWidth))
        {
            ApplyTabWidth();
        }
    }

    /// <summary>
    /// Stash the right-click target line + side on the ContextMenu's
    /// <c>Tag</c> as a <see cref="HunkActionContext"/> so each MenuItem's
    /// <c>CommandParameter</c> binding can pick it up. Run also evaluates
    /// per-hunk-action visibility flags on the pane VM so the menu items
    /// hide cleanly when the caret isn't in a hunk.
    /// </summary>
    private void HandleEditorContextMenuOpening(TextEditor editor, ChangeSide side, ContextMenuEventArgs e)
    {
        if (editor.ContextMenu is null || _vm is null) return;
        int line = editor.TextArea.Caret.Line; // 1-based
        var ctx = new HunkActionContext(side, line);
        editor.ContextMenu.Tag = ctx;

        // Push state into the pane VM so the visibility bindings update
        // before the menu actually pops up.
        _vm.UpdateRightClickContext(ctx);
    }

    private void OnLeftEditorContextMenuOpening(object sender, ContextMenuEventArgs e) =>
        HandleEditorContextMenuOpening(LeftEditor, ChangeSide.Left, e);

    private void OnRightEditorContextMenuOpening(object sender, ContextMenuEventArgs e) =>
        HandleEditorContextMenuOpening(RightEditor, ChangeSide.Right, e);

    private void OnInlineEditorContextMenuOpening(object sender, ContextMenuEventArgs e) =>
        HandleEditorContextMenuOpening(InlineEditor, ChangeSide.Right, e);

    private void ApplyHighlightMap()
    {
        if (_vm is null) return;
        var map = _vm.HighlightMap;

        if (_leftBg is not null) _leftBg.LineHighlights = map.LeftLines;
        if (_rightBg is not null) _rightBg.LineHighlights = map.RightLines;
        if (_leftIntra is not null) _leftIntra.LineHighlights = map.LeftLines;
        if (_rightIntra is not null) _rightIntra.LineHighlights = map.RightLines;
        if (_inlineBg is not null) _inlineBg.LineHighlights = _vm.InlineLineHighlights;
        if (_inlineIntra is not null) _inlineIntra.LineHighlights = _vm.InlineLineHighlights;

        ApplySyntaxHighlighting();

        LeftEditor.TextArea.TextView.Redraw();
        RightEditor.TextArea.TextView.Redraw();
        InlineEditor.TextArea.TextView.Redraw();
    }

    /// <summary>
    /// Pick a <see cref="IHighlightingDefinition"/> for each editor based on
    /// the current file's extension. The right / inline editors get the
    /// new-side extension; the left editor gets the old-side extension (the
    /// two only differ for renames, and the extension usually doesn't change
    /// across a rename, but this is the principled choice). Unknown
    /// extensions resolve to <c>null</c>, which renders plain text.
    ///
    /// <para>The diff-coloring renderers paint cell backgrounds, so they
    /// compose cleanly on top of the foreground colors that syntax
    /// highlighting applies. The intra-line colorizer also sets a foreground
    /// (so changed words win over the language coloring), which is the
    /// intended ranking — diff signal trumps language signal.</para>
    /// </summary>
    private void ApplySyntaxHighlighting()
    {
        if (_vm is null) return;
        var change = _vm.CurrentEntry?.Change;
        string newPath = change?.Path ?? string.Empty;
        string oldPath = change?.OldPath ?? newPath;

        LeftEditor.SyntaxHighlighting = ResolveHighlighting(oldPath);
        RightEditor.SyntaxHighlighting = ResolveHighlighting(newPath);
        InlineEditor.SyntaxHighlighting = ResolveHighlighting(newPath.Length > 0 ? newPath : oldPath);
    }

    private static IHighlightingDefinition? ResolveHighlighting(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        string ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return null;
        return HighlightingManager.Instance.GetDefinitionByExtension(ext);
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

    /// <summary>
    /// Bridge <see cref="DiffPaneViewModel.TabWidth"/> to AvalonEdit's
    /// <c>TextEditor.Options.IndentationSize</c> on all three editors.
    /// <c>IndentationSize</c> is not a dependency property so it can't
    /// be reached from XAML directly. Setting it triggers AvalonEdit's
    /// own redraw, so we don't need to call <c>Redraw()</c> here.
    /// </summary>
    private void ApplyTabWidth()
    {
        if (_vm is null) return;
        int width = _vm.TabWidth;
        if (width < 1) width = 1;
        LeftEditor.Options.IndentationSize = width;
        RightEditor.Options.IndentationSize = width;
        InlineEditor.Options.IndentationSize = width;
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
