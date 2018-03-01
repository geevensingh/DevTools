namespace JsonViewer.Commands
{
    public class ToggleCommand : BaseCommand
    {
        private bool _isChecked = false;

        public ToggleCommand(string text, bool isChecked)
            : base(text, true)
        {
            this.IsChecked = isChecked;
        }

        public bool IsChecked { get => _isChecked; protected set => this.SetValue(ref _isChecked, value, "IsChecked"); }
    }
}
