namespace JsonViewer.Commands
{
    using System.Windows.Input;

    public class BaseCommandBinding : CommandBinding
    {
        public BaseCommandBinding(RoutedUICommand routedUICommand)
            : base(routedUICommand)
        {
            this.CanExecute += OnCanExecute;
            this.Executed += OnExecuted;
        }

        private void OnExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            (e.Command as BaseRoutedUICommand).BaseCommand.Execute(e.Parameter);
        }

        private void OnCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = (e.Command as BaseRoutedUICommand).BaseCommand.CanExecute(e.Parameter);
        }
    }
}
