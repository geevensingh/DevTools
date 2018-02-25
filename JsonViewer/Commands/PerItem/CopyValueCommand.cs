namespace JsonViewer.Commands.PerItem
{
    using System.Windows;
    using JsonViewer.View;

    internal class CopyValueCommand : BaseTreeViewDataCommand
    {
        public CopyValueCommand(TreeViewData data)
            : base(data, "Copy value", true)
        {
        }

        public override void Execute(object parameter)
        {
            Clipboard.SetText(this.Data.Value);
        }
    }
}
