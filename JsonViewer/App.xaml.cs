namespace JsonViewer
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Windows;

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private List<string> _args = null;
        private string _initialText = string.Empty;
        private List<MainWindow> _windows = new List<MainWindow>();

        internal delegate void MainWindowChangedHandler(App sender);

        internal event MainWindowChangedHandler MainWindowChanged;

        public IList<string> Args { get => _args; }

        public string InitialText { get => _initialText; set => _initialText = value; }

        internal static new App Current { get => (App)Application.Current; }

        internal new MainWindow MainWindow { get => (MainWindow)base.MainWindow; private set => base.MainWindow = value; }

        internal void AddWindow(MainWindow window)
        {
            Debug.Assert(!_windows.Contains(window));
            _windows.Add(window);
            window.GotFocus += OnWindowGotFocus;
            SetMainWindow(window);
        }

        internal void RemoveWindow(MainWindow window)
        {
            Debug.Assert(_windows.Contains(window));
            _windows.Remove(window);
            window.GotFocus -= OnWindowGotFocus;
            if (this.MainWindow == window && _windows.Count > 0)
            {
                this.SetMainWindow(_windows[_windows.Count - 1]);
                this.MainWindow.Focus();
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _args = new List<string>(e?.Args);

            if (_args.Count > 0)
            {
                string launchFilePath = _args[0];
                if (File.Exists(launchFilePath))
                {
                    _initialText = File.ReadAllText(launchFilePath);
                }
            }
        }

        private void SetMainWindow(MainWindow window)
        {
            if (this.MainWindow != window)
            {
                this.MainWindow = window;
                this.MainWindowChanged?.Invoke(this);
            }
        }

        private void OnWindowGotFocus(object sender, RoutedEventArgs e)
        {
            MainWindow window = (MainWindow)sender;
            Debug.Assert(_windows.Contains(window));

            // Move the window to the end of the list
            _windows.Remove(window);
            _windows.Add(window);

            this.SetMainWindow(window);
        }
    }
}
