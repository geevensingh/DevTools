namespace JsonViewer.Commands
{
    using System;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Input;
    using JsonViewer.Model;
    using JsonViewer.View;
    using Utilities;

    public abstract class BaseCommand : NotifyPropertyChanged, ICommand
    {
        private string _text = string.Empty;
        private bool _canExecute = false;
        private TabContent _tab = null;

        public BaseCommand(string text)
            : this(text, false)
        {
        }

        public BaseCommand(string text, bool canExecute)
        {
            Debug.Assert(System.Threading.Thread.CurrentThread.ManagedThreadId == 1);

            _text = text;
            _canExecute = canExecute;

            this.RoutedUICommand = new BaseRoutedUICommand(this);
            this.CommandBinding = new BaseCommandBinding(this.RoutedUICommand);
        }

        public event EventHandler CanExecuteChanged;

        public RoutedUICommand RoutedUICommand { get; private set; }

        public CommandBinding CommandBinding { get; private set; }

        public string Text { get => _text; set => _text = value; }

        public bool IsEnabled { get => this.CanExecute(null); }

        public Visibility IsVisible
        {
            get
            {
                if (this.ForceVisibility.HasValue)
                {
                    return this.ForceVisibility.Value;
                }

                return this.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        protected Visibility? ForceVisibility { get; set; }

        protected TabContent Tab
        {
            get
            {
                return _tab;
            }

            set
            {
                FileLogger.Assert(_tab == null);
                _tab = value;
                _tab.PropertyChanged += (sender, evt) => this.OnTabPropertyChanged(evt.PropertyName);
            }
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute;
        }

        public abstract void Execute(object parameter);

        protected void AddKeyGesture(KeyGesture keyGesture)
        {
            this.RoutedUICommand.InputGestures.Add(keyGesture);
        }

        protected void SetCanExecute(bool canExecute)
        {
            if (this.SetValue(ref _canExecute, canExecute, new string[] { "CanExecute", "IsEnabled", "IsVisible" }))
            {
                this.CanExecuteChanged?.Invoke(this, new EventArgs());
            }
        }

        protected virtual void OnTabPropertyChanged(string propertyName)
        {
        }

        private void SetText(string text)
        {
            this.SetValue(ref _text, text, "Text");
        }
    }
}
