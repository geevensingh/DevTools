namespace JsonViewer.Commands.PerItem
{
    using JsonViewer;

    internal class BaseTreeViewDataCommand : BaseCommand
    {
        private TreeViewData _data;

        public BaseTreeViewDataCommand(TreeViewData data, string text, bool canExecute)
            : base(text, canExecute)
        {
            _data = data;
            _data.PropertyChanged += OnDataPropertyChanged;
        }

        internal TreeViewData Data { get => _data; }

        protected virtual void OnDataPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
        }
    }
}
