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
            this.Update();
        }

        private MainWindow.DisplayMode DisplayMode { get; set; }

        public override void Execute(object parameter)
        {
            this.MainWindow.SetDisplayMode(this.DisplayMode);
        }

        protected override void OnMainWindowPropertyChanged(string propertyName)
        {
            switch (propertyName)
            {
                case "Mode":
                    this.Update();
                    break;
            }
        }

        private void Update()
        {
            this.SetCanExecute(this.MainWindow.Mode != this.DisplayMode);
        }
    }
}
