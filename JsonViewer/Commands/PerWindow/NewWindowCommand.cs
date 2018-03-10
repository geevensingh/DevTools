namespace JsonViewer.Commands.PerWindow
{
    using System.Windows.Input;

    public class NewWindowCommand : BaseCommand
    {
        public NewWindowCommand(MainWindow mainWindow)
            : base("New window", true)
        {
            this.MainWindow = mainWindow;

            this.AddKeyGesture(new KeyGesture(Key.N, ModifierKeys.Control));
        }

        public override void Execute(object parameter)
        {
            this.MainWindow.ShowNewWindow();
        }
    }
}
