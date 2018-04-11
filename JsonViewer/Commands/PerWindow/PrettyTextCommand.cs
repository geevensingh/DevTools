namespace JsonViewer.Commands.PerWindow
{
    using System.Diagnostics;
    using JsonViewer.Model;
    using JsonViewer.View;

    public class PrettyTextCommand : BaseCommand
    {
        public PrettyTextCommand(MainWindow mainWindow)
            : base("Pretty-ify text")
        {
            this.ForceVisibility = System.Windows.Visibility.Visible;
            this.MainWindow = mainWindow;
            this.Update();
        }

        public override void Execute(object parameter)
        {
            FileLogger.Assert(this.MainWindow.RootObject != null);
            this.MainWindow.Raw_TextBox.Text = this.MainWindow.RootObject?.PrettyValueString;
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
