namespace JsonViewer
{
    using System;
    using System.ComponentModel;
    using System.Deployment.Application;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private Finder _finder;
        private Point? _initialOffset = null;
        private RootObject _rootObject = null;

        public MainWindow()
        {
            _finder = new Finder(this);

            InitializeComponent();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public Finder Finder { get => _finder; }

        internal RootObject RootObject { get => _rootObject; }

        public void ShowNewWindow()
        {
            this.SaveWindowPosition();

            MainWindow newWindow = new MainWindow
            {
                _initialOffset = new Point(20, 20)
            };
            newWindow.Show();
        }

        public async Task<bool> ReloadAsync()
        {
            JsonObjectFactory factory = new JsonObjectFactory();
            RootObject rootObject = await factory.Parse(this.Raw_TextBox.Text);
            if (rootObject == null)
            {
                this.SetErrorMessage("Unable to parse given string");
                return false;
            }

            this.SetErrorMessage(string.Empty);
            _rootObject = rootObject;
            NotifyPropertyChanged.FirePropertyChanged("RootObject", this, this.PropertyChanged);

            _finder.SetObjects(rootObject);
            _rootObject.SetTreeItemsSource(this.Tree);

            if (_rootObject.TotalChildCount <= 50)
            {
                this.Tree.ExpandAll();
            }

            return true;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowPlacementSerializer.SetPlacement(this, Properties.Settings.Default.MainWindowPlacement, _initialOffset);
            if (_initialOffset.HasValue)
            {
                this.SaveWindowPosition();
            }
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
            string initialText = App.Current.InitialText;
            App.Current.InitialText = string.Empty;

            if (string.IsNullOrEmpty(initialText) && Clipboard.ContainsText())
            {
                string jsonString = Clipboard.GetText();
                if (JsonObjectFactory.TryDeserialize(jsonString) != null)
                {
                    initialText = jsonString;
                }
            }

            this.Raw_TextBox.Text = initialText;
        }

        private void SetErrorMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                this.Banner.Visibility = Visibility.Collapsed;
            }
            else
            {
                this.Banner.Text = message;
                this.Banner.Visibility = Visibility.Visible;
            }
        }

        private async void Raw_TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Debug.Assert(sender.Equals(this.Raw_TextBox));
            bool succeeded = await this.ReloadAsync();
            if (!succeeded)
            {
                // Fix json copied from iScope scripts
                string text = this.Raw_TextBox.Text.Trim();
                if (text.StartsWith("\"") && text.EndsWith("\""))
                {
                    text = text.Trim(new char[] { '"' });
                    text = text.Replace("\"\"", "\"");
                    this.Raw_TextBox.Text = text;
                }
            }
        }

        private void Tree_CommandBinding_Copy(object sender, ExecutedRoutedEventArgs e)
        {
            if (this.Tree.SelectedItem is TreeViewData selectedData)
            {
                Clipboard.SetText(selectedData.Value);
            }
        }

        private void CommandBinding_Find(object sender, ExecutedRoutedEventArgs e)
        {
            _finder.ShowWindow();
        }

        private void CheckForUpdates(object sender, RoutedEventArgs e)
        {
            UpdateCheckInfo info = null;

            if (!ApplicationDeployment.IsNetworkDeployed)
            {
                MessageBox.Show("This application was not network deployed.", "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ApplicationDeployment ad = ApplicationDeployment.CurrentDeployment;

            try
            {
                info = ad.CheckForDetailedUpdate();
            }
            catch (DeploymentDownloadException dde)
            {
                MessageBox.Show("The new version of the application cannot be downloaded at this time. \n\nPlease check your network connection, or try again later. Error: " + dde.Message, "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            catch (InvalidDeploymentException ide)
            {
                MessageBox.Show("Cannot check for a new version of the application. The ClickOnce deployment is corrupt. Please redeploy the application and try again. Error: " + ide.Message, "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            catch (InvalidOperationException ioe)
            {
                MessageBox.Show("This application cannot be updated. It is likely not a ClickOnce application. Error: " + ioe.Message, "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!info.UpdateAvailable)
            {
                MessageBox.Show("This application is up to date.", "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool doUpdate = true;

            if (!info.IsUpdateRequired)
            {
                MessageBoxResult dr = MessageBox.Show("An update is available. Would you like to update the application now?", "Update Available", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (dr != MessageBoxResult.OK)
                {
                    doUpdate = false;
                }
            }
            else
            {
                // Display a message that the app MUST reboot. Display the minimum required version.
                MessageBox.Show(
                    "This application has detected a mandatory update from your current " +
                    "version to version " + info.MinimumRequiredVersion.ToString() +
                    ". The application will now install the update and restart.",
                    "Update Available",
                    button: MessageBoxButton.OK,
                    icon: MessageBoxImage.Information);
            }

            if (doUpdate)
            {
                try
                {
                    ad.Update();
                    MessageBox.Show("The application has been upgraded, and will now restart.", "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                    System.Windows.Forms.Application.Restart();
                    Application.Current.Shutdown();
                }
                catch (DeploymentDownloadException dde)
                {
                    MessageBox.Show("Cannot install the latest version of the application. \n\nPlease check your network connection, or try again later. Error: " + dde, "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
        }
    }
}
