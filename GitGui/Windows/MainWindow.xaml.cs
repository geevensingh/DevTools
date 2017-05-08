using System;
using System.Collections.Generic;
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
using GitGui.Windows;

namespace GitGui.Windows
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            LinearGradientBrush background = new LinearGradientBrush(Color.FromRgb(40, 40, 40), Color.FromRgb(215, 215, 215), 45.0d);
            this.Background = background;
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.Owner = this;
            settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            settingsWindow.Width = Math.Max(500, this.Width - 50);
            settingsWindow.Height = Math.Max(300, this.Height - 100);
            //settingsWindow.SizeToContent = SizeToContent.WidthAndHeight;

            bool? dialogResult = settingsWindow.ShowDialog();
            if (dialogResult.HasValue && dialogResult.Value)
            {
                ReloadSettings();
            }
        }

        private void ReloadSettings()
        {
        }
    }
}
