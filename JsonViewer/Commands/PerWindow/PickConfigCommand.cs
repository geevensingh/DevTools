namespace JsonViewer.Commands.PerWindow
{
    using System.Windows.Input;

    public class PickConfigCommand : BaseCommand
    {
        private MainWindow _mainWindow = null;

        public PickConfigCommand(MainWindow mainWindow)
            : base("Pick config", true)
        {
            _mainWindow = mainWindow;

            this.AddKeyGesture(new KeyGesture(Key.L, ModifierKeys.Control));
        }

        public override void Execute(object parameter)
        {
            string filePath = OpenJsonFileCommand.PickJsonFile(_mainWindow, "Pick config file");
            if (!string.IsNullOrEmpty(filePath))
            {
                _mainWindow.LoadConfig(filePath);
            }
        }
    }
}
