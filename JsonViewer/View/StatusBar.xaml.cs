namespace JsonViewer.View
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Deployment.Application;
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

    /// <summary>
    /// Interaction logic for StatusBar.xaml
    /// </summary>
    public partial class StatusBar : UserControl, INotifyPropertyChanged
    {
        private MainWindow _mainWindow = null;

        public StatusBar()
        {
            InitializeComponent();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string CurrentIndex
        {
            get
            {
                int? currentIndex = _mainWindow?.Tree?.SelectedIndex;
                if (currentIndex.HasValue)
                {
                    return currentIndex.Value.ToString();
                }

                return "--";
            }
        }

        public string CurrentVersion
        {
            get
            {
                string result = ApplicationDeployment.CurrentDeployment?.CurrentVersion?.ToString();
                if (string.IsNullOrEmpty(result))
                {
                    result = "unknown";
                }
                return result;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _mainWindow = (MainWindow)this.DataContext;
            Debug.Assert(_mainWindow != null);

            _mainWindow.Tree.PropertyChanged += OnTreePropertyChanged;
        }

        private void OnTreePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "SelectedIndex":
                    NotifyPropertyChanged.FirePropertyChanged("CurrentIndex", this, this.PropertyChanged);
                    break;
            }
        }
    }
}
