namespace JsonViewer.Commands.PerItem
{
    using System.Windows;
    using JsonViewer;

    internal class ExpandAllCommand : BaseTreeViewDataCommand
    {
        public ExpandAllCommand(TreeViewData data)
            : base(data, "Expand all", data.HasChildren)
        {
        }

        public override void Execute(object parameter)
        {
            App.Current.MainWindow.Tree.ExpandSubtree(this.Data);
        }

        protected override void OnDataPropertyChanged(string propertyName)
        {
            switch (propertyName)
            {
                case "AllChildren":
                    this.SetCanExecute(this.Data.HasChildren);
                    break;
            }
        }
    }
}
