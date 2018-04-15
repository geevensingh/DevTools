namespace JsonViewer.Commands.PerWindow
{
    using JsonViewer.View;

    public class SettingsCommand : BaseCommand
    {
        public SettingsCommand(TabContent mainWindow)
            : base("Settings...", true)
        {
            this.Tab = mainWindow;
        }

        public override void Execute(object parameter)
        {
            SettingsWindow settingsWindow = new SettingsWindow(this.Tab);
            settingsWindow.ShowDialog();
        }
    }
}
