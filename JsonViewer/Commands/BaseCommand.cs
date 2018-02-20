namespace JsonViewer.Commands
{
    using System;
    using System.Windows;
    using System.Windows.Input;

    internal class BaseCommand : NotifyPropertyChanged, ICommand
    {
        private string _text = string.Empty;
        private bool _canExecute = false;

        public BaseCommand(string text)
            : this(text, false)
        {
        }

        public BaseCommand(string text, bool canExecute)
        {
            _text = text;
            _canExecute = canExecute;

            App.Current.MainWindowChanged += OnMainWindowChanged;
        }

        public event EventHandler CanExecuteChanged;

        public string Text { get => _text; set => _text = value; }

        public bool IsEnabled { get => this.CanExecute(null); }

        public Visibility IsVisible { get => this.IsEnabled ? Visibility.Visible : Visibility.Collapsed; }

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
            if (this.SetValue(ref _canExecute, canExecute, new string[] { "CanExecute", "IsEnabled", "IsVisible" }))
            {
                this.CanExecuteChanged?.Invoke(this, new EventArgs());
            }
        }

        protected virtual void OnMainWindowChanged(App sender)
        {
        }

        private void SetText(string text)
        {
            this.SetValue(ref _text, text, "Text");
        }
    }
}
