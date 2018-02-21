namespace JsonViewer.Commands.PerWindow
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Utilities;

    public class ReloadCommand : BaseCommand
    {
        private MainWindow _mainWindow = null;

        public ReloadCommand(MainWindow mainWindow)
            : base("Reload", true)
        {
            _mainWindow = mainWindow;
        }

        public override void Execute(object parameter)
        {
            Config.Reload();
            _mainWindow.ReloadAsync().Forget();
        }
    }
}
