using System.Windows.Controls;
using DiffViewer.Rendering;

namespace DiffViewer.Views;

/// <summary>
/// Code-behind for <see cref="DiffPaneView"/>. Wires up the
/// <see cref="TextEditorScrollSync"/> between the two editors after they're
/// templated; everything else is XAML-only data binding.
/// </summary>
public partial class DiffPaneView : UserControl
{
    private TextEditorScrollSync? _scrollSync;

    public DiffPaneView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _scrollSync ??= new TextEditorScrollSync(LeftEditor, RightEditor);
        };
        Unloaded += (_, _) =>
        {
            _scrollSync?.Dispose();
            _scrollSync = null;
        };
    }
}
