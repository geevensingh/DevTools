namespace JsonViewer.Commands
{
    using System.Windows.Input;

    public class BaseRoutedUICommand : RoutedUICommand
    {
        public BaseRoutedUICommand(BaseCommand baseCommand, KeyGesture keyGesture)
        {
            this.BaseCommand = baseCommand;
            this.Text = baseCommand.Text;
            this.InputGestures.Add(keyGesture);
        }

        public BaseCommand BaseCommand { get; private set; }
    }
}
