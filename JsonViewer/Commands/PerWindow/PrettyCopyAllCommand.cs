namespace JsonViewer.Commands.PerWindow
{
    using System;
    using System.Diagnostics;
    using System.Windows;
    using JsonViewer.Model;

    public class PrettyCopyAllCommand : BaseCommand
    {
        private MainWindow _mainWindow;

        public PrettyCopyAllCommand(MainWindow mainWindow)
            : base("Copy pretty value (beta)", false)
        {
            _mainWindow = mainWindow;
            _mainWindow.PropertyChanged += OnMainWindowPropertyChanged;
            this.Update();
        }

        public override void Execute(object parameter)
        {
            Debug.Assert(_mainWindow.RootObject != null);
            Clipboard.SetDataObject(_mainWindow.RootObject?.PrettyValueString);
        }

        private void Update()
        {
            this.SetCanExecute(_mainWindow.RootObject != null);
        }

        private void OnMainWindowPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "RootObject":
                    this.Update();
                    break;
            }
        }
    }
}
