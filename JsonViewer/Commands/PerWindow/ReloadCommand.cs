namespace JsonViewer.Commands.PerWindow
{
    using System.Windows.Input;
    using Utilities;

    public class ReloadCommand : BaseCommand
    {
        public ReloadCommand(MainWindow mainWindow)
            : base("Reload", true)
        {
            this.MainWindow = mainWindow;

            this.AddKeyGesture(new KeyGesture(Key.F5));
        }

        public override void Execute(object parameter)
        {
            Config.Reload();
            this.MainWindow.ReloadAsync().Forget();
        }
    }
}
