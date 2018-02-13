using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.ObjectModel;

namespace JsonViewer
{
    class RootObject : JsonObject
    {
        ObservableCollection<TreeViewData> _viewChildren = null;

        public RootObject() : base(string.Empty, string.Empty)
        {
        }

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

        protected override void RebuildViewObjects(JsonObject child)
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
            base.AddChild(child);
        }
    }
}
