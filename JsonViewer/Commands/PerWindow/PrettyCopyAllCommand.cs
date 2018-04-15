namespace JsonViewer.Commands.PerWindow
{
    using System.Diagnostics;
    using System.Windows;
    using JsonViewer.Model;
    using JsonViewer.View;

    public class PrettyCopyAllCommand : BaseCommand
    {
        public PrettyCopyAllCommand(TabContent mainWindow)
            : base("Copy pretty value (beta)", false)
        {
            this.Tab = mainWindow;
            this.Update();
        }

        public override void Execute(object parameter)
        {
            FileLogger.Assert(this.Tab.RootObject != null);
            Clipboard.SetDataObject(this.Tab.RootObject?.PrettyValueString);
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
            this.SetCanExecute(this.Tab.RootObject != null);
        }
    }
}
