namespace JsonViewer.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class SwitchModeCommand : BaseCommand
    {
        public SwitchModeCommand(MainWindow mainWindow, string text, MainWindow.DisplayMode displayMode)
            : base(text)
        {
            this.DisplayMode = displayMode;
            this.MainWindow = mainWindow;
            this.MainWindow.PropertyChanged += OnMainWindowPropertyChanged;
            this.Update();
        }

        private void OnMainWindowPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "Mode":
                    this.Update();
                    break;
                default:
                    break;
            }
        }

        private MainWindow MainWindow { get; set; }

        private MainWindow.DisplayMode DisplayMode { get; set; }

        public override void Execute(object parameter)
        {
            this.MainWindow.SetDisplayMode(this.DisplayMode);
        }

        private void Update()
        {
            this.SetCanExecute(this.MainWindow.Mode != this.DisplayMode);
        }
    }
}
