namespace JsonViewer.Commands
{
    using System.Diagnostics;

    public class PrettyTextCommand : BaseCommand
    {
        public PrettyTextCommand(MainWindow mainWindow)
            : base("Pretty-ify text")
        {
            this.MainWindow = mainWindow;
            this.Update();
        }

        public override void Execute(object parameter)
        {
            Debug.Assert(this.MainWindow.RootObject != null);
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
