namespace JsonViewer.Commands.PerItem
{
    using System.Windows;
    using JsonViewer.View;

    public class CopyValueCommand : BaseTreeViewDataCommand
    {
        public CopyValueCommand(TreeViewData data)
            : base(data, "Copy value", true)
        {
        }

        public override void Execute(object parameter)
        {
            Clipboard.SetDataObject(this.Data.Value);
        }
    }
}
