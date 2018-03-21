namespace JsonViewer.Commands.PerWindow
{
    using JsonViewer.View;

    public class SettingsCommand : BaseCommand
    {
        public SettingsCommand(MainWindow mainWindow)
            : base("Settings...", true)
        {
            this.MainWindow = mainWindow;
        }

        public override void Execute(object parameter)
        {
            SettingsWindow settingsWindow = new SettingsWindow(this.MainWindow);
            settingsWindow.ShowDialog();
        }
    }
}
