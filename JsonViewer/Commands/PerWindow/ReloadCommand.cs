namespace JsonViewer.Commands.PerWindow
{
    using System.Windows.Input;
    using JsonViewer.Model;
    using JsonViewer.View;
    using Utilities;

    public class ReloadCommand : BaseCommand
    {
        public ReloadCommand()
            : base("Reload", true)
        {
            this.AddKeyGesture(new KeyGesture(Key.F5));
        }

        public override void Execute(object parameter)
        {
            Config.Reload();
        }
    }
}
