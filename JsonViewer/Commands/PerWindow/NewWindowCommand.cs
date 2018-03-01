namespace JsonViewer.Commands.PerWindow
{
    using System.Windows.Input;

    public class NewWindowCommand : BaseCommand
    {
        private MainWindow _mainWindow = null;

        public NewWindowCommand(MainWindow mainWindow)
            : base("New window", true)
        {
            _mainWindow = mainWindow;

            this.AddKeyGesture(new KeyGesture(Key.N, ModifierKeys.Control));
        }

        public override void Execute(object parameter)
        {
            _mainWindow.ShowNewWindow();
        }
    }
}
