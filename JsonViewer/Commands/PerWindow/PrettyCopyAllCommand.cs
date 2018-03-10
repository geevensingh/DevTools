namespace JsonViewer.Commands.PerWindow
{
    using System;
    using System.Diagnostics;
    using System.Windows;
    using JsonViewer.Model;

    public class PrettyCopyAllCommand : BaseCommand
    {
        public PrettyCopyAllCommand(MainWindow mainWindow)
            : base("Copy pretty value (beta)", false)
        {
            this.MainWindow = mainWindow;
            this.Update();
        }

        public override void Execute(object parameter)
        {
            Debug.Assert(this.MainWindow.RootObject != null);
            Clipboard.SetDataObject(this.MainWindow.RootObject?.PrettyValueString);
        }

        protected override void OnMainWindowPropertyChanged(string propertyName)
        {
            switch (propertyName)
            {
                case "RootObject":
                    this.Update();
                    break;
            }
        }

        private void Update()
        {
            this.SetCanExecute(this.MainWindow.RootObject != null);
        }
    }
}
