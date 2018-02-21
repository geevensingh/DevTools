namespace JsonViewer.Commands.PerWindow
{
    using System.Windows;
    using Utilities;

    public class PickConfigCommand : BaseCommand
    {
        private MainWindow _mainWindow = null;

        public PickConfigCommand(MainWindow mainWindow)
            : base("Pick Config", true)
        {
            _mainWindow = mainWindow;
        }

        public override void Execute(object parameter)
        {
            string filePath = OpenJsonFileCommand.PickJsonFile(_mainWindow, "Pick config file");
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
    }
}
