namespace JsonViewer.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows;
    using Utilities;

    internal class PickConfigCommand : BaseCommand
    {
        public PickConfigCommand()
            : base("Pick Config", true)
        {
        }

        public override void Execute(object parameter)
        {
            MainWindow mainWindow = App.Current.MainWindow;
            string filePath = OpenJsonFileCommand.PickJsonFile(mainWindow, "Pick config file");
            if (!string.IsNullOrEmpty(filePath))
            {
                if (Config.SetPath(filePath))
                {
                    mainWindow.ReloadAsync().Forget();
                }
                else
                {
                    MessageBox.Show(mainWindow, "Unable to load config: " + filePath, "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
