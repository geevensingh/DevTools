namespace JsonViewer.Commands.PerItem
{
    using System.Diagnostics;
    using JsonViewer.View;

    internal abstract class BaseTreeViewDataCommand : BaseCommand
    {
        private TreeViewData _data;

        public BaseTreeViewDataCommand(TreeViewData data, string text, bool canExecute)
            : base(text, canExecute)
        {
            _data = data;
            _data.PropertyChanged += OnDataPropertyChanged;
        }

        internal TreeViewData Data { get => _data; }

        protected virtual void OnDataPropertyChanged(string propertyName)
        {
        }

        private void OnDataPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Debug.Assert(sender == _data);
            this.OnDataPropertyChanged(e.PropertyName);
        }
    }
}
