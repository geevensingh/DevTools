using System.Windows.Controls;
using DiffViewer.Rendering;
using DiffViewer.ViewModels;

namespace DiffViewer.Views;

/// <summary>
/// Code-behind for <see cref="DiffPaneView"/>. Wires up:
/// <list type="bullet">
/// <item>The <see cref="TextEditorScrollSync"/> between the two editors after they're templated.</item>
/// <item>One <see cref="DiffBackgroundRenderer"/> + <see cref="IntraLineColorizer"/> per editor,
/// re-pointed at <see cref="DiffPaneViewModel.HighlightMap"/> on every load.</item>
/// </list>
/// Everything else is XAML-only data binding.
/// </summary>
public partial class DiffPaneView : UserControl
{
    private TextEditorScrollSync? _scrollSync;
    private DiffBackgroundRenderer? _leftBg;
    private DiffBackgroundRenderer? _rightBg;
    private IntraLineColorizer? _leftIntra;
    private IntraLineColorizer? _rightIntra;
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

        AttachToViewModel(DataContext as DiffPaneViewModel);
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _scrollSync?.Dispose();
        _scrollSync = null;

        DetachFromViewModel();

        if (_leftBg is not null)
        {
            LeftEditor.TextArea.TextView.BackgroundRenderers.Remove(_leftBg);
            _leftBg = null;
        }
        if (_rightBg is not null)
        {
            RightEditor.TextArea.TextView.BackgroundRenderers.Remove(_rightBg);
            _rightBg = null;
        }
        if (_leftIntra is not null)
        {
            LeftEditor.TextArea.TextView.LineTransformers.Remove(_leftIntra);
            _leftIntra = null;
        }
        if (_rightIntra is not null)
        {
            RightEditor.TextArea.TextView.LineTransformers.Remove(_rightIntra);
            _rightIntra = null;
        }
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
        ApplyHighlightMap();
    }

    private void DetachFromViewModel()
    {
        if (_vm is null) return;
        _vm.HighlightMapChanged -= OnHighlightMapChanged;
        _vm = null;
    }

    private void OnHighlightMapChanged(object? sender, EventArgs e) => ApplyHighlightMap();

    private void ApplyHighlightMap()
    {
        if (_vm is null) return;
        var map = _vm.HighlightMap;

        if (_leftBg is not null) _leftBg.LineHighlights = map.LeftLines;
        if (_rightBg is not null) _rightBg.LineHighlights = map.RightLines;
        if (_leftIntra is not null) _leftIntra.LineHighlights = map.LeftLines;
        if (_rightIntra is not null) _rightIntra.LineHighlights = map.RightLines;

        LeftEditor.TextArea.TextView.Redraw();
        RightEditor.TextArea.TextView.Redraw();
    }
}
