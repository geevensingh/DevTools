namespace JsonViewer
{
    using System.Windows.Input;
    using JsonViewer.Commands;

    internal static class CommandFactory
    {
        public static readonly NewWindowCommand NewWindow = new NewWindowCommand();

        public static readonly ReloadCommand Reload = new ReloadCommand();

        public static readonly PickConfigCommand PickConfig = new PickConfigCommand();

        public static readonly HideFindCommand HideFind = new HideFindCommand();

        public static readonly OpenJsonFileCommand OpenJsonFile = new OpenJsonFileCommand();

        public static readonly HighlightParentsCommand HighlightParents = new HighlightParentsCommand();

        public static readonly ExpandAllCommand ExpandAll = new ExpandAllCommand();

        public static readonly CollapseAllCommand CollapseAll = new CollapseAllCommand();

        public static readonly ShowToolbarTextCommand ShowToolbarText = new ShowToolbarTextCommand();

        public static readonly ShowToolbarIconCommand ShowToolbarIcon = new ShowToolbarIconCommand();
    }
}
