namespace JsonViewer.Commands.PerWindow
{
    using JsonViewer.View;

    public class SettingsCommand : BaseCommand
    {
        public SettingsCommand(TabContent tab)
            : base("Settings...", true)
        {
            this.Tab = tab;
        }

        public override void Execute(object parameter)
        {
            SettingsWindow settingsWindow = new SettingsWindow(this.Tab.Window);
            settingsWindow.ShowDialog();
        }
    }
}
