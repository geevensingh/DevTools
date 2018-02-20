namespace JsonViewer.Commands.PerItem
{
    using System.Windows;
    using JsonViewer;

    internal class CollapseAllCommand : BaseTreeViewDataCommand
    {
        public CollapseAllCommand(TreeViewData data)
            : base(data, "Collapse All", data.CanCollapse)
        {
        }

        public override void Execute(object parameter)
        {
            App.Current.MainWindow.Tree.CollapseSubtree(this.Data);
        }
    }
}
