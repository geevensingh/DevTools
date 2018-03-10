namespace JsonViewer.Commands.PerWindow
{
    using System.Windows.Input;
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
            string filePath = OpenJsonFileCommand.PickJsonFile(this.MainWindow, "Pick config file");
            if (!string.IsNullOrEmpty(filePath))
            {
                this.MainWindow.LoadConfig(filePath);
            }
        }
    }
}
