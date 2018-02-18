namespace JsonViewer
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;

    internal class RootObject : JsonObject
    {
        private ObservableCollection<TreeViewData> _viewChildren = null;
        private List<JsonObject> _allChildren = null;

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

        internal List<JsonObject> AllChildren
        {
            get
            {
                if (_allChildren == null)
                {
                    _allChildren = new List<JsonObject>(this.GetAllChildren());
                }

                return _allChildren;
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
            _allChildren = null;
        }
    }
}
