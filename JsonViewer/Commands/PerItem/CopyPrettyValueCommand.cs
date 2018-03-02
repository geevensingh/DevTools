namespace JsonViewer.Commands.PerItem
{
    using System.Windows;
    using JsonViewer.View;

    internal class CopyPrettyValueCommand : BaseTreeViewDataCommand
    {
        public CopyPrettyValueCommand(TreeViewData data)
            : base(data, "Copy pretty value (beta)", data.HasChildren)
        {
        }

        public override void Execute(object parameter)
        {
            Clipboard.SetDataObject(this.Data.PrettyValue);
        }
    }
}
