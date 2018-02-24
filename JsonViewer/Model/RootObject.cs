namespace JsonViewer
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.Threading.Tasks;

    internal class RootObject : JsonObject
    {
        private ObservableCollection<TreeViewData> _viewChildren = null;

        public RootObject()
            : base(string.Empty, string.Empty)
        {
        }

        public static async Task<RootObject> Create(string jsonString)
        {
            return await Task.Run(
                () =>
                {
                    System.Web.Script.Serialization.JavaScriptSerializer ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                    Dictionary<string, object> jsonObj = JsonObjectFactory.TryDeserialize(jsonString);
                    RootObject root = new RootObject();
                    var jsonObjects = new List<JsonObject>();
                    JsonObjectFactory.Flatten(ref jsonObjects, jsonObj, root);
                    return root;
                });
        }

        public override void AddChildren(IList<JsonObject> children)
        {
            Debug.Assert(_viewChildren == null);
            _viewChildren = null;
            base.AddChildren(children);
        }

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
    }
}
