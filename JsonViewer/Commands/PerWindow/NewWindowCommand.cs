namespace JsonViewer.Commands.PerWindow
{
    using System.Windows.Input;
    using JsonViewer.View;

    public class NewWindowCommand : BaseCommand
    {
        public NewWindowCommand(TabContent mainWindow)
            : base("New window", true)
        {
            this.Tab = mainWindow;

            this.AddKeyGesture(new KeyGesture(Key.N, ModifierKeys.Control));
        }

        public override void Execute(object parameter)
        {
            this.Tab.ShowNewWindow();
        }
    }
}
