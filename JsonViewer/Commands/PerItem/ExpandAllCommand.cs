namespace JsonViewer.Commands.PerItem
{
    using System.Windows;
    using JsonViewer;

    internal class ExpandAllCommand : BaseTreeViewDataCommand
    {
        public ExpandAllCommand(TreeViewData data)
            : base(data, "Expand all", data.CanExpand)
        {
        }

        public override void Execute(object parameter)
        {
            App.Current.MainWindow.Tree.ExpandSubtree(this.Data);
        }
    }
}
