namespace JsonViewer.Commands.PerItem
{
    using System.Windows;
    using JsonViewer.View;

    internal class CopyKeyCommand : BaseTreeViewDataCommand
    {
        public CopyKeyCommand(TreeViewData data)
            : base(data, "Copy key", true)
        {
        }

        public override void Execute(object parameter)
        {
            Clipboard.SetText(this.Data.KeyName);
        }
    }
}
