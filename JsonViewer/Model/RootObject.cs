namespace JsonViewer
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;

    internal class RootObject : JsonObject
    {
        private ObservableCollection<TreeViewData> _viewChildren = null;

        public RootObject()
            : base(string.Empty, string.Empty)
        {
        }

        public override RootObject Root { get => this; }

        internal void SetTreeItemsSource(CustomTreeView tree)
        {
            if (_viewChildren == null)
            {
                _viewChildren = TreeViewDataFactory.CreateCollection(tree, this);
            }

            Debug.Assert(_viewChildren != null);
            Debug.Assert(_viewChildren.Count == 0 || _viewChildren[0].Tree == tree);
            tree.ItemsSource = _viewChildren;
        }

        protected override void UpdateChild(JsonObject child)
        {
            Debug.Assert(this.Children.Contains(child));
            int index = this.Children.IndexOf(child);
            Debug.Assert(this.Children[index].ViewObject == _viewChildren[index]);
            _viewChildren.RemoveAt(index);
            _viewChildren.Insert(index, child.ResetView());
        }

        public override void AddChildren(IList<JsonObject> children)
        {
            Debug.Assert(_viewChildren == null);
            _viewChildren = null;
            base.AddChildren(children);
        }
    }
}
