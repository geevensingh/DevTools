namespace JsonViewer.Commands
{
    using System;
    using System.Windows.Input;

    internal class BaseCommand : NotifyPropertyChanged, ICommand
    {
        private string _text = string.Empty;
        private bool _canExecute = false;

        public BaseCommand()
            : this(string.Empty, false)
        {
        }

        public BaseCommand(string text)
            : this(text, false)
        {
        }

        public BaseCommand(string text, bool canExecute)
        {
            _text = text;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public string Text { get => _text; set => _text = value; }

        public bool CanExecute(object parameter)
        {
            return _canExecute;
        }

        public virtual void Execute(object parameter)
        {
            throw new NotImplementedException();
        }

        protected void SetCanExecute(bool canExecute)
        {
            if (canExecute != _canExecute)
            {
                _canExecute = canExecute;
                this.CanExecuteChanged?.Invoke(this, new EventArgs());
            }
        }

        protected void SetText(string text)
        {
            this.SetValue(ref _text, text, "Text");
        }
    }
}
