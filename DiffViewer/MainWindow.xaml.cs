using System.Windows;
using System.Windows.Input;
using DiffViewer.ViewModels;
using DiffViewer.Views;

namespace DiffViewer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is MainViewModel vm)
        {
            vm.ShowSettingsHandler = ShowSettingsDialog;
            vm.ConfirmHandler = ShowConfirmDialog;
            vm.ToastHandler = ShowToast;
            vm.FocusCycleRequested = CycleFocusAcrossPanes;
        }
    }

    /// <summary>
    /// 3-stop focus cycle: file list → left diff editor → right diff editor →
    /// file list. Implemented via direct FocusManager calls so AvalonEdit's
    /// internal Tab handling stays unaffected. If focus is somewhere
    /// unexpected (e.g. inside an open dialog), the cycle resets to the
    /// file list as the fallback start point.
    /// </summary>
    private void CycleFocusAcrossPanes()
    {
        // Find the three target controls by name. They live in named
        // resources in their respective Views; we walk the visual tree.
        var fileList = FindDescendant<System.Windows.Controls.ListBox>("FileListBox")
                       ?? FindDescendant<System.Windows.Controls.TreeView>("FileListTree")
                       ?? FindDescendant<System.Windows.Controls.ItemsControl>(null);
        var leftEditor  = FindDescendant<ICSharpCode.AvalonEdit.TextEditor>("LeftEditor");
        var rightEditor = FindDescendant<ICSharpCode.AvalonEdit.TextEditor>("RightEditor");

        var stops = new System.Collections.Generic.List<System.Windows.IInputElement>();
        if (fileList    is not null) stops.Add(fileList);
        if (leftEditor  is not null) stops.Add(leftEditor);
        if (rightEditor is not null) stops.Add(rightEditor);
        if (stops.Count == 0) return;

        var focused = FocusManager.GetFocusedElement(this);
        int currentIndex = focused is null ? -1 : stops.IndexOf(focused);
        var next = stops[(currentIndex + 1) % stops.Count];
        next.Focus();
    }

    private T? FindDescendant<T>(string? name) where T : DependencyObject
    {
        return FindDescendantCore<T>(this, name);
    }

    private static T? FindDescendantCore<T>(DependencyObject root, string? name) where T : DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                if (name is null) return match;
                if (child is FrameworkElement fe && fe.Name == name) return match;
            }
            var deep = FindDescendantCore<T>(child, name);
            if (deep is not null) return deep;
        }
        return null;
    }

    /// <summary>
    /// Esc cascading-close: closes the closest find-panel first.
    /// 1. If the focused diff pane has its find panel open, close it.
    /// 2. Else if the other diff pane has its find open, close it.
    /// 3. Else clear the file-list selection.
    /// </summary>
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;

        var leftEditor  = FindDescendant<ICSharpCode.AvalonEdit.TextEditor>("LeftEditor");
        var rightEditor = FindDescendant<ICSharpCode.AvalonEdit.TextEditor>("RightEditor");
        var focused = FocusManager.GetFocusedElement(this);

        // Walk focus → other → fall through to selection clear.
        var focusOwner = WhichEditor(focused, leftEditor, rightEditor);
        var ordered = focusOwner switch
        {
            "left"  => new[] { leftEditor, rightEditor },
            "right" => new[] { rightEditor, leftEditor },
            _       => new[] { leftEditor, rightEditor },
        };
        foreach (var ed in ordered)
        {
            if (TryCloseFindPanel(ed)) { e.Handled = true; return; }
        }
        if (DataContext is MainViewModel vm && vm.FileList.SelectedEntry is not null)
        {
            vm.FileList.SelectedEntry = null;
            e.Handled = true;
        }
    }

    private static string? WhichEditor(IInputElement? focused,
        ICSharpCode.AvalonEdit.TextEditor? left,
        ICSharpCode.AvalonEdit.TextEditor? right)
    {
        if (focused is null) return null;
        if (left  is not null && IsLogicalDescendant(left,  focused as DependencyObject)) return "left";
        if (right is not null && IsLogicalDescendant(right, focused as DependencyObject)) return "right";
        return null;
    }

    private static bool IsLogicalDescendant(DependencyObject ancestor, DependencyObject? candidate)
    {
        while (candidate is not null)
        {
            if (ReferenceEquals(candidate, ancestor)) return true;
            candidate = System.Windows.Media.VisualTreeHelper.GetParent(candidate)
                        ?? System.Windows.LogicalTreeHelper.GetParent(candidate);
        }
        return false;
    }

    private static bool TryCloseFindPanel(ICSharpCode.AvalonEdit.TextEditor? editor)
    {
        if (editor is null) return false;
        // AvalonEdit's SearchPanel attaches itself as a logical child of the
        // TextArea once Ctrl+F has opened it once. We discover any open
        // SearchPanel by walking the visual tree under the editor and
        // checking IsVisible / IsClosed via reflection-friendly API.
        var panel = FindDescendantCore<ICSharpCode.AvalonEdit.Search.SearchPanel>(editor, null);
        if (panel is null) return false;
        // The Reopen / Close methods are public; IsClosed is internal, so
        // we just call Close() which is idempotent on an already-closed
        // panel and returns false in that case via the visual-tree probe
        // (we won't find a visual). Visibility check guards against
        // double-handling Esc when the panel is logically attached but
        // not currently shown.
        if (!panel.IsVisible) return false;
        panel.Close();
        return true;
    }


    private ConfirmationResult ShowConfirmDialog(ConfirmationRequest request)
    {
        var dialog = new ConfirmDialog(request) { Owner = this };
        dialog.ShowDialog();
        return dialog.Result;
    }

    private void ShowToast(string message)
    {
        // v1: simple status-line surface via the title bar; richer toast UX
        // is in the polish phase. Always marshal to the UI thread.
        Dispatcher.BeginInvoke(() =>
        {
            var prevTitle = Title;
            Title = $"DiffViewer — {message}";
            // Best-effort restore after 4s; not exact, but good enough
            // for a v1 status line.
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = System.TimeSpan.FromSeconds(4),
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (Title.StartsWith("DiffViewer — " + message))
                {
                    Title = prevTitle;
                }
            };
            timer.Start();
        });
    }

    private void ShowSettingsDialog()
    {
        if (DataContext is not MainViewModel vm || vm.SettingsService is null) return;

        var dialogVm = new SettingsViewModel(
            vm.SettingsService,
            confirmReset: prompt => MessageBox.Show(
                this, prompt, "Reset settings",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK,
            availableFonts: DiffViewer.Rendering.SystemFontEnumerator.Enumerate());

        var dialog = new SettingsDialog(dialogVm) { Owner = this };
        try { dialog.ShowDialog(); }
        finally { dialogVm.Dispose(); }
    }
}
