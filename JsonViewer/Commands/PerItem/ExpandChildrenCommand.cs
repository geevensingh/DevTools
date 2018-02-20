namespace JsonViewer.Commands.PerItem
{
    using System.Windows;
    using JsonViewer;

    internal class ExpandChildrenCommand : BaseTreeViewDataCommand
    {
        public ExpandChildrenCommand(TreeViewData data)
            : base(data, "Expand children", data.CanExpandChildren)
        {
        }

        public override void Execute(object parameter)
        {
            App.Current.MainWindow.Tree.ExpandChildren(this.Data);
        }
    }
}
