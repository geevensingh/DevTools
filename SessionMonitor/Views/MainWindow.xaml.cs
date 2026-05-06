using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CopilotSessionMonitor.ViewModels;

namespace CopilotSessionMonitor.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _saveDebounce;
    private bool _settingsApplied;

    public MainWindow(MainWindowViewModel vm, AppSettings settings)
    {
        InitializeComponent();
        DataContext = vm;
        _vm = vm;
        _settings = settings;

        // Apply persisted size + pin state BEFORE the window is rendered so we
        // don't get a flash at the default size. Position is applied separately
        // in OnSourceInitialized once we can clamp to the actual screen bounds.
        Width = settings.WindowWidth;
        Height = settings.WindowHeight;
        _vm.IsPinned = settings.IsPinned;
        _vm.ShowOffline = settings.ShowOffline;
        _vm.GroupByRepo = settings.GroupByRepo;

        _vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainWindowViewModel.IsPinned):
                    Topmost = _vm.IsPinned;
                    _settings.IsPinned = _vm.IsPinned;
                    ScheduleSave();
                    break;
                case nameof(MainWindowViewModel.ShowOffline):
                    _settings.ShowOffline = _vm.ShowOffline;
                    ScheduleSave();
                    break;
                case nameof(MainWindowViewModel.GroupByRepo):
                    _settings.GroupByRepo = _vm.GroupByRepo;
                    ScheduleSave();
                    break;
            }
        };
        Topmost = _vm.IsPinned;

        // Debounce settings writes so dragging the window doesn't hammer the disk.
        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveDebounce.Tick += (_, _) =>
        {
            _saveDebounce.Stop();
            _settings.Save();
        };

        SizeChanged += OnSizeOrLocationChanged;
        LocationChanged += (_, _) => OnSizeOrLocationChanged(this, EventArgs.Empty);

        InputBindings.Clear();
        InputBindings.Add(new KeyBinding(
            new RelayDelegateCommand(FocusFilterBox),
            new KeyGesture(Key.F, ModifierKeys.Control)));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplySavedPosition();
        _settingsApplied = true;
    }

    private void ApplySavedPosition()
    {
        if (_settings.WindowLeft is { } left && _settings.WindowTop is { } top)
        {
            // Clamp to the virtual screen so an unplugged monitor doesn't
            // park the window off-screen.
            var virt = SystemParameters.VirtualScreenLeft;
            var virtW = SystemParameters.VirtualScreenWidth;
            var virtH = SystemParameters.VirtualScreenHeight;
            var virtT = SystemParameters.VirtualScreenTop;
            // Require at least 80 px of the window to remain on-screen horizontally
            // and vertically; otherwise discard the saved position.
            if (left + 80 < virt || left > virt + virtW - 80 ||
                top + 40 < virtT || top > virtT + virtH - 40)
            {
                _settings.WindowLeft = null;
                _settings.WindowTop = null;
            }
            else
            {
                Left = left;
                Top = top;
            }
        }
    }

    private void OnSizeOrLocationChanged(object? sender, EventArgs e)
    {
        if (!_settingsApplied) return;
        if (WindowState != WindowState.Normal) return; // ignore minimized/maximized
        _settings.WindowWidth = Width;
        _settings.WindowHeight = Height;
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    /// <summary>Force a save right now (used on app shutdown).</summary>
    public void FlushSettings()
    {
        _saveDebounce.Stop();
        _settings.Save();
    }

    private void FocusFilterBox()
    {
        if (FilterBox is null) return;
        FilterBox.Focus();
        FilterBox.SelectAll();
    }

    private void FilterBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _vm.ClearFilterCommand.Execute(null);
            Keyboard.ClearFocus();
            FocusManager.SetFocusedElement(this, this);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            _vm.ExpandTopVisibleRowCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            // Move focus into the list and select the first visible row.
            if (SessionsList is null) return;
            SessionsList.Focus();
            if (SessionsList.Items.Count > 0)
            {
                SessionsList.SelectedIndex = 0;
                if (SessionsList.ItemContainerGenerator.ContainerFromIndex(0) is System.Windows.Controls.ListBoxItem item)
                    item.Focus();
            }
            e.Handled = true;
        }
    }

    private void SessionsList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Enter expands the selected row.
        if (e.Key == Key.Enter)
        {
            if (SessionsList?.SelectedItem is ViewModels.SessionRowViewModel row)
            {
                row.IsExpanded = !row.IsExpanded;
                e.Handled = true;
            }
        }
        // / typed → focus filter (no modifier needed; common power-user shortcut)
        else if (e.Key == Key.OemQuestion && Keyboard.Modifiers == ModifierKeys.None)
        {
            FocusFilterBox();
            e.Handled = true;
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => Hide();
    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();

    public event EventHandler? SettingsRequested;
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Show window. If no saved position exists, snap to the bottom-right above the tray.</summary>
    public void ShowAtTray()
    {
        if (_settings.WindowLeft is null || _settings.WindowTop is null)
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 12;
            Top = area.Bottom - Height - 12;
        }
        Show();
        Activate();
        Focus();
    }

    /// <summary>Bring the window forward and scroll-into-view + expand the row matching <paramref name="sessionId"/>.
    /// Used by the toast click handler so the user lands on the session they were notified about.</summary>
    public void RevealSession(string sessionId)
    {
        ShowAtTray();
        if (string.IsNullOrEmpty(sessionId)) return;
        foreach (var item in _vm.Sessions)
        {
            if (string.Equals(item.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            {
                item.IsExpanded = true; // single-expansion handler will collapse others
                SessionsList?.ScrollIntoView(item);
                return;
            }
        }
    }

    private sealed class RelayDelegateCommand : ICommand
    {
        private readonly Action _act;
        public RelayDelegateCommand(Action act) => _act = act;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _act();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
