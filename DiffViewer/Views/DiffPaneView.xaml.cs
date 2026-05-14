using System.ComponentModel;
using System.IO;
using System.Windows.Controls;
using DiffViewer.Rendering;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using ViewportState = DiffViewer.Models.ViewportState;

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

        SubscribeViewportEvents();

        AttachToViewModel(DataContext as DiffPaneViewModel);
    }

    /// <summary>
    /// Subscribe to AvalonEdit scroll + layout events on all three
    /// editors so the hunk bar's viewport indicator can track the
    /// visible window in real time. Both events are needed:
    /// <list type="bullet">
    ///   <item><c>ScrollOffsetChanged</c> fires on every scroll, but
    ///   <c>VisualLines</c> may not yet reflect the new offset.</item>
    ///   <item><c>VisualLinesChanged</c> fires after each layout pass,
    ///   which is when <c>VisualLines</c> is authoritative for the
    ///   currently-visible range — and the only chance to compute a
    ///   first viewport on initial load before any user scroll.</item>
    /// </list>
    /// </summary>
    private void SubscribeViewportEvents()
    {
        LeftEditor.TextArea.TextView.ScrollOffsetChanged += OnEditorViewportChanged;
        RightEditor.TextArea.TextView.ScrollOffsetChanged += OnEditorViewportChanged;
        InlineEditor.TextArea.TextView.ScrollOffsetChanged += OnEditorViewportChanged;
        LeftEditor.TextArea.TextView.VisualLinesChanged += OnEditorViewportChanged;
        RightEditor.TextArea.TextView.VisualLinesChanged += OnEditorViewportChanged;
        InlineEditor.TextArea.TextView.VisualLinesChanged += OnEditorViewportChanged;
    }

    private void UnsubscribeViewportEvents()
    {
        LeftEditor.TextArea.TextView.ScrollOffsetChanged -= OnEditorViewportChanged;
        RightEditor.TextArea.TextView.ScrollOffsetChanged -= OnEditorViewportChanged;
        InlineEditor.TextArea.TextView.ScrollOffsetChanged -= OnEditorViewportChanged;
        LeftEditor.TextArea.TextView.VisualLinesChanged -= OnEditorViewportChanged;
        RightEditor.TextArea.TextView.VisualLinesChanged -= OnEditorViewportChanged;
        InlineEditor.TextArea.TextView.VisualLinesChanged -= OnEditorViewportChanged;
    }

    private void OnEditorViewportChanged(object? sender, EventArgs e) => UpdateViewport();

    /// <summary>
    /// Recompute <see cref="DiffPaneViewModel.Viewport"/> from the
    /// currently-visible editor lines. In side-by-side mode the left/right
    /// ranges come straight from the two editors. In inline mode both
    /// ranges are projected through <see cref="DiffPaneViewModel.InlineLineToSourceLines"/>:
    /// pick the first non-null OldLine/NewLine inside the visible window
    /// to seed Left/Right first; the last non-null Old/NewLine to seed
    /// Left/Right last. A side with no entries inside the window gets 0,
    /// which the geometry layer renders as "no rect for that column".
    /// </summary>
    private void UpdateViewport()
    {
        if (_vm is null) return;

        if (_vm.IsSideBySide)
        {
            if (!TryGetVisibleRange(LeftEditor, out int lFirst, out int lLast) ||
                !TryGetVisibleRange(RightEditor, out int rFirst, out int rLast))
            {
                _vm.Viewport = null;
                return;
            }
            _vm.Viewport = new ViewportState(lFirst, lLast, rFirst, rLast);
        }
        else
        {
            if (!TryGetVisibleRange(InlineEditor, out int first, out int last))
            {
                _vm.Viewport = null;
                return;
            }
            var map = _vm.InlineLineToSourceLines;
            if (map is null || map.Count == 0)
            {
                _vm.Viewport = null;
                return;
            }
            int oldFirst = 0, oldLast = 0, newFirst = 0, newLast = 0;
            int lo = first - 1; // map is 0-indexed; inline doc lines are 1-indexed
            int hi = last - 1;
            if (lo < 0) lo = 0;
            if (hi >= map.Count) hi = map.Count - 1;
            for (int i = lo; i <= hi; i++)
            {
                var (oldLn, newLn) = map[i];
                if (oldLn is int o)
                {
                    if (oldFirst == 0) oldFirst = o;
                    oldLast = o;
                }
                if (newLn is int n)
                {
                    if (newFirst == 0) newFirst = n;
                    newLast = n;
                }
            }
            _vm.Viewport = new ViewportState(oldFirst, oldLast, newFirst, newLast);
        }
    }

    /// <summary>
    /// Read the first/last fully-visible 1-based line numbers from an
    /// AvalonEdit editor. Returns false when:
    /// <list type="bullet">
    ///   <item>The editor hasn't completed a layout pass yet (VisualLines
    ///   is empty before the first measure + arrange).</item>
    ///   <item>The visual lines are <em>invalid</em> mid-scroll —
    ///   <c>ScrollOffsetChanged</c> fires before AvalonEdit rebuilds
    ///   <c>VisualLines</c> for the new offset, and accessing the
    ///   property in that window throws <c>VisualLinesInvalidException</c>.
    ///   <c>VisualLinesChanged</c> fires after the rebuild completes, so
    ///   the viewport gets a correct sample on the very next pump.</item>
    /// </list>
    /// </summary>
    private static bool TryGetVisibleRange(TextEditor editor, out int first, out int last)
    {
        first = 0;
        last = 0;
        var view = editor.TextArea.TextView;
        if (!view.VisualLinesValid) return false;
        var visible = view.VisualLines;
        if (visible is null || visible.Count == 0) return false;
        first = visible[0].FirstDocumentLine.LineNumber;
        last = visible[visible.Count - 1].LastDocumentLine.LineNumber;
        return true;
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

        UnsubscribeViewportEvents();
        if (_vm is not null) _vm.Viewport = null;

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
        _vm.ScrollRequested += OnScrollRequested;
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
        _vm.ScrollRequested -= OnScrollRequested;
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
        else if (e.PropertyName == nameof(DiffPaneViewModel.IsSideBySide))
        {
            // Mode switch: the freshly-shown editor's visible range may
            // differ from the one we last sampled. Recompute now so the
            // bar's viewport indicator doesn't lag a frame behind.
            UpdateViewport();
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

    /// <summary>
    /// Handle a viewport-band click on the hunk overview bar. Differs
    /// from <see cref="OnHunkNavigationRequested"/> in three ways:
    /// <list type="bullet">
    ///   <item>It does NOT move the caret — viewport scroll is a passive
    ///   navigation; caret position is the user's editing point.</item>
    ///   <item>It does NOT update <c>CurrentHunkIndex</c> — the viewport
    ///   indicator is independent of hunk selection.</item>
    ///   <item>Inline mode uses an inverse lookup through
    ///   <c>InlineLineToSourceLines</c> to find the inline-doc line that
    ///   maps to the requested new-side source line.</item>
    /// </list>
    /// </summary>
    private void OnScrollRequested(object? sender, ScrollRequestedEventArgs e)
    {
        if (_vm is null) return;
        if (_vm.IsSideBySide)
        {
            ScrollEditorToLineNoCaret(LeftEditor, e.LeftLine);
            ScrollEditorToLineNoCaret(RightEditor, e.RightLine);
        }
        else
        {
            int inlineLine = MapNewLineToInline(e.RightLine);
            ScrollEditorToLineNoCaret(InlineEditor, inlineLine);
        }
    }

    /// <summary>
    /// Inverse lookup from a new-side source line to an inline-document
    /// line. Walks <see cref="DiffPaneViewModel.InlineLineToSourceLines"/>
    /// for the first entry whose <c>NewLine &gt;= target</c>. Returns the
    /// last inline line if every entry is below the target (target is
    /// past EOF on the new side).
    /// </summary>
    private int MapNewLineToInline(int newLine)
    {
        if (_vm?.InlineLineToSourceLines is not { Count: > 0 } map) return newLine;
        for (int i = 0; i < map.Count; i++)
        {
            if (map[i].NewLine is int n && n >= newLine) return i + 1;
        }
        return map.Count;
    }

    private static void ScrollEditorToLineNoCaret(TextEditor editor, int line)
    {
        if (line < 1) return;
        if (editor.Document is null) return;
        if (line > editor.Document.LineCount) line = editor.Document.LineCount;
        if (line < 1) return;
        editor.ScrollToLine(line);
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
