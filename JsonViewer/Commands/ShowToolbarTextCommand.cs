namespace JsonViewer.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    internal class ShowToolbarTextCommand : BaseCommand
    {
        public ShowToolbarTextCommand()
            : base("Show toolbar text", true)
        {
        }

        public override void Execute(object parameter)
        {
            bool newValue = !Properties.Settings.Default.MainWindowToolbarTextVisible;
            if (!newValue)
            {
                Properties.Settings.Default.MainWindowToolbarIconVisible = true;
            }

            Properties.Settings.Default.MainWindowToolbarTextVisible = newValue;
            Properties.Settings.Default.Save();
        }
    }
}
