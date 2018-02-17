namespace JsonViewer.Commands
{
    internal class NewWindowCommand : BaseCommand
    {
        public NewWindowCommand()
            : base("New Window", true)
        {
        }

        public override void Execute(object parameter)
        {
            App.Current.MainWindow.ShowNewWindow();
        }
    }
}
