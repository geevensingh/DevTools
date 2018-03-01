namespace JsonViewer.Commands.PerWindow
{
    using System.Windows.Input;
    using Utilities;

    public class ReloadCommand : BaseCommand
    {
        private MainWindow _mainWindow = null;

        public ReloadCommand(MainWindow mainWindow)
            : base("Reload", true)
        {
            _mainWindow = mainWindow;

            this.AddKeyGesture(new KeyGesture(Key.F5));
        }

        public override void Execute(object parameter)
        {
            Config.Reload();
            _mainWindow.ReloadAsync().Forget();
        }
    }
}
