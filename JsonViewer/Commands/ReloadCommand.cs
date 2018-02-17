namespace JsonViewer.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Utilities;

    internal class ReloadCommand : BaseCommand
    {
        public ReloadCommand()
            : base("Reload", true)
        {
        }

        public override void Execute(object parameter)
        {
            Config.Reload();
            App.Current.MainWindow.ReloadAsync().Forget();
        }
    }
}
