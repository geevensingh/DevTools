namespace JsonViewer
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Data;
    using System.Windows.Documents;
    using System.Windows.Input;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;
    using System.Windows.Navigation;
    using System.Windows.Shapes;
    using Microsoft.Win32;
    using Utilities;

    /// <summary>
    /// Interaction logic for MainWindowToolbar.xaml
    /// </summary>
    public partial class MainWindowToolbar : UserControl, INotifyPropertyChanged
    {
        private MainWindow _mainWindow = null;

        public MainWindowToolbar()
        {
            InitializeComponent();

            Properties.Settings.Default.PropertyChanged += OnSettingsPropertyChanged;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public Visibility ToolbarTextVisibility { get => Properties.Settings.Default.MainWindowToolbarTextVisible ? Visibility.Visible : Visibility.Collapsed; }

        public Visibility ToolbarIconVisibility { get => Properties.Settings.Default.MainWindowToolbarIconVisible ? Visibility.Visible : Visibility.Collapsed; }

        private void OnSettingsPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "MainWindowToolbarTextVisible":
                    NotifyPropertyChanged.FirePropertyChanged("ToolbarTextVisibility", this, this.PropertyChanged);
                    break;
                case "MainWindowToolbarIconVisible":
                    NotifyPropertyChanged.FirePropertyChanged("ToolbarIconVisibility", this, this.PropertyChanged);
                    break;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _mainWindow = (MainWindow)this.DataContext;
            Debug.Assert(_mainWindow != null);

            _mainWindow.Finder.PropertyChanged += OnFinderPropertyChanged;
            FindTextBox.Text = _mainWindow.Finder.Text;

            this.HighlightParentsButton.IsChecked = Properties.Settings.Default.HighlightSelectedParents;
        }

        private void OnFinderPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Debug.Assert(sender == _mainWindow.Finder);
            if (e.PropertyName == "Text")
            {
                this.FindTextBox.Text = _mainWindow.Finder.Text;
            }
        }

        private void ExpandAllButton_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.Tree.ExpandAll();
        }

        private void CollapseAllButton_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.Tree.CollapseAll();
        }

        private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _mainWindow.Finder.Text = FindTextBox.Text;
        }

        private void Tree_CommandBinding_Find(object sender, ExecutedRoutedEventArgs e)
        {

        }

        private void Tree_CommandBinding_HideFind(object sender, ExecutedRoutedEventArgs e)
        {

        }

        public void PickConfig_CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            string filePath = this.PickJsonFile();
            if (!string.IsNullOrEmpty(filePath))
            {
                if (Config.SetPath(filePath))
                {
                    _mainWindow.ReloadAsync().Forget();
                }
                else
                {
                    MessageBox.Show(_mainWindow, "Unable to load config: " + filePath, "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public void Reload_CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Config.Reload();
            _mainWindow.ReloadAsync().Forget();
        }

        public void NewWindow_CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            _mainWindow.ShowNewWindow();
        }

        public void OpenJsonFile_CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            string filePath = this.PickJsonFile();
            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    _mainWindow.Raw_TextBox.Text = System.IO.File.ReadAllText(filePath);
                }
                catch
                {
                }
            }
        }

        public void HighlightParents_CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Properties.Settings.Default.HighlightSelectedParents = !Properties.Settings.Default.HighlightSelectedParents;
            Properties.Settings.Default.Save();
            this.HighlightParentsButton.IsChecked = Properties.Settings.Default.HighlightSelectedParents;
            TreeViewData selected = _mainWindow.Tree.SelectedValue as TreeViewData;
            if (selected != null)
            {
                selected.IsSelected = selected.IsSelected;
            }
        }

        private string PickJsonFile()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Json files (*.json)|*.json|All files (*.*)|*.*"
            };
            bool? ofdResult = openFileDialog.ShowDialog(_mainWindow);
            if (ofdResult.HasValue && ofdResult.Value)
            {
                return openFileDialog.FileName;
            }

            return null;
        }

        static int foo = 0;
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            switch (foo++ % 3)
            {
                case 0:
                    Properties.Settings.Default.MainWindowToolbarTextVisible = true;
                    Properties.Settings.Default.MainWindowToolbarIconVisible = true;
                    break;
                case 1:
                    Properties.Settings.Default.MainWindowToolbarTextVisible = true;
                    Properties.Settings.Default.MainWindowToolbarIconVisible = false;
                    break;
                case 2:
                    Properties.Settings.Default.MainWindowToolbarTextVisible = false;
                    Properties.Settings.Default.MainWindowToolbarIconVisible = true;
                    break;
            }
            Properties.Settings.Default.Save();
        }
    }
}
