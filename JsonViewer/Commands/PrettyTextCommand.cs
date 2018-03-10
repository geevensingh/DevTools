namespace JsonViewer.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class PrettyTextCommand : BaseCommand
    {
        public PrettyTextCommand(MainWindow mainWindow) : base("Pretty-ify text")
        {
            this.MainWindow = mainWindow;
            this.MainWindow.PropertyChanged += OnMainWindowPropertyChanged;
            this.Update();
        }

        private void OnMainWindowPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "Mode":
                case "RootObject":
                    this.Update();
                    break;
            }
        }

        private void Update()
        {
            this.SetCanExecute(this.MainWindow.RootObject != null && this.MainWindow.Mode == MainWindow.DisplayMode.RawText);
        }

        public override void Execute(object parameter)
        {
            Debug.Assert(this.MainWindow.RootObject != null);
            this.MainWindow.Raw_TextBox.Text = this.MainWindow.RootObject?.PrettyValueString;
        }

        private MainWindow MainWindow;
    }
}
