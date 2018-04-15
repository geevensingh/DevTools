namespace JsonViewer.View
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Controls;
    using JsonViewer.Model;

    /// <summary>
    /// Interaction logic for Window.xaml
    /// </summary>
    public partial class Window : System.Windows.Window
    {
        private Point? _initialOffset = null;

        public Window()
        {
            InitializeComponent();
        }

        internal ClipboardManager ClipboardManager { get; private set; }

        public void LoadConfig(string filePath)
        {
            Properties.Settings.Default.PropertyChanged -= OnSettingsPropertyChanged;
            bool succeeded = Config.SetPath(filePath);
            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;

            if (succeeded)
            {
                Debug.Assert(false);
                // Should really tell *all* tabs to reload
                //this.ReloadAsync().Forget();
            }
            else
            {
                MessageBox.Show(this, "Unable to load config: " + filePath, "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnSettingsPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            //switch (e.PropertyName)
            //{
            //    case "ConfigPath":
            //        this.SetWarningMessage(
            //            "Your default config file has changed.  Would you like to reload?",
            //            () =>
            //            {
            //                this.Toolbar.ReloadCommand.Execute(null);
            //                this.UpdateWarnings();
            //            },
            //            () =>
            //            {
            //                this.UpdateWarnings();
            //            });
            //        break;
            //    case "MainWindowWarnOnDefaultConfig":
            //        this.UpdateWarnings();
            //        break;
            //}
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            this.ClipboardManager = new ClipboardManager(this);

            WindowPlacementSerializer.SetPlacement(this, Properties.Settings.Default.MainWindowPlacement, _initialOffset);
            if (_initialOffset.HasValue)
            {
                this.SaveWindowPosition();
            }

            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            this.SaveWindowPosition();
            base.OnClosing(e);
        }

        private void SaveWindowPosition()
        {
            Properties.Settings.Default.MainWindowPlacement = WindowPlacementSerializer.GetPlacement(this);
            Properties.Settings.Default.Save();
        }


        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            tabablzControl.NewItemFactory = () =>
            {
                TabContent tab = new TabContent();
                tab.DataContext = this;

                TabItem tabItem = new TabItem();
                tabItem.Header = "new tab";
                tabItem.Content = tab;
                return tabItem;
            };
        }
    }
}
