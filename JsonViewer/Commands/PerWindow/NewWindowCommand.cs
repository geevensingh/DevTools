namespace JsonViewer.Commands.PerWindow
{
    public class NewWindowCommand : BaseCommand
    {
        private MainWindow _mainWindow = null;

        public NewWindowCommand(MainWindow mainWindow)
            : base("New Window", true)
        {
            _mainWindow = mainWindow;
        }

        public override void Execute(object parameter)
        {
            _mainWindow.ShowNewWindow();
        }
    }
}
