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

        internal ObservableCollection<TreeViewData> ViewChildren
        {
            get
            {
                if (_viewChildren == null)
                {
                    _viewChildren = TreeViewDataFactory.CreateCollection(this);
                }

                Debug.Assert(_viewChildren != null);
                return _viewChildren;
            }
        }

        protected override void UpdateChild(JsonObject child)
        {
            Debug.Assert(this.Children.Contains(child));
            int index = this.Children.IndexOf(child);
            Debug.Assert(this.Children[index].ViewObject == _viewChildren[index]);
            _viewChildren.RemoveAt(index);
            _viewChildren.Insert(index, child.ResetView());
        }

        protected override void AddChild(JsonObject child)
        {
            Debug.Assert(_viewChildren == null);
            _viewChildren = null;
            base.AddChild(child);
        }
    }
}
