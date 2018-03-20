namespace JsonViewer.Commands.PerWindow
{
    using System.IO;
    using System.Windows.Input;
    using JsonViewer.Model;
    using JsonViewer.View;

    public class PickConfigCommand : BaseCommand
    {
        public PickConfigCommand(MainWindow mainWindow)
            : base("Pick config", true)
        {
            this.MainWindow = mainWindow;

            this.AddKeyGesture(new KeyGesture(Key.L, ModifierKeys.Control));
        }

        public override void Execute(object parameter)
        {
            string filePath = OpenJsonFileCommand.PickJsonFile(this.MainWindow, "Pick config file", this.GetInitialDirectory());
            if (!string.IsNullOrEmpty(filePath))
            {
                this.MainWindow.LoadConfig(filePath);
            }
        }

        private string GetInitialDirectory()
        {
            string configPath = Config.This.FilePath;
            string configDirectory = string.IsNullOrEmpty(configPath) ? string.Empty : Path.GetFullPath(configPath);
            return string.IsNullOrEmpty(configDirectory) ? string.Empty : Path.GetDirectoryName(configDirectory);
        }
    }
}
