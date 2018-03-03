namespace JsonViewer.Commands
{
    using System.Windows.Input;

    public class BaseRoutedUICommand : RoutedUICommand
    {
        public BaseRoutedUICommand(BaseCommand baseCommand)
        {
            this.BaseCommand = baseCommand;
            this.Text = baseCommand.Text;
        }

        public BaseCommand BaseCommand { get; private set; }
    }
}
