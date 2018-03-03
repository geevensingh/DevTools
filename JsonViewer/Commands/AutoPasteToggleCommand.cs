namespace JsonViewer.Commands
{
    using System.Windows.Input;
    using JsonViewer.Commands.PerWindow;
    using JsonViewer.Model;

    public class AutoPasteToggleCommand : ToggleCommand
    {
        private PasteCommand _pasteCommand;

        public AutoPasteToggleCommand(PasteCommand pasteCommand)
            : base("Auto-paste", false)
        {
            _pasteCommand = pasteCommand;
            _pasteCommand.CanExecuteChanged += OnPasteCommandCanExecuteChanged;
        }

        public override void Execute(object parameter)
        {
            this.IsChecked = !this.IsChecked;
            this.AttemptExecute();
        }

        private void OnPasteCommandCanExecuteChanged(object sender, System.EventArgs e)
        {
            this.AttemptExecute();
        }

        private void AttemptExecute()
        {
            if (this.IsChecked && _pasteCommand.CanExecute(null))
            {
                _pasteCommand.Execute(null);
            }
        }
    }
}
