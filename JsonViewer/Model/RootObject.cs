namespace JsonViewer
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading.Tasks;
    using JsonViewer.View;
    using Utilities;

    public class RootObject : JsonObject
    {
        private ObservableCollection<TreeViewData> _viewChildren = null;

        public RootObject()
            : base(string.Empty, new Dictionary<string, object>())
        {
        }

        public static async Task<RootObject> Create(string jsonString)
        {
            using (new WaitCursor())
            {
                return await Task.Run(
                    () =>
                    {
                        System.Web.Script.Serialization.JavaScriptSerializer ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                        Dictionary<string, object> jsonObj = JsonObjectFactory.TryDeserialize(jsonString);
                        if (jsonObj == null)
                        {
                            return null;
                        }

                        RootObject root = new RootObject();
                        var jsonObjects = new List<JsonObject>();
                        JsonObjectFactory.Flatten(ref jsonObjects, jsonObj, root);
                        return root;
                    });
            }
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

            foreach (JsonObject jsonObj in this.AllChildren)
            {
                ConfigRule rule = jsonObj.Rules.FirstOrDefault(x => x.ExpandChildren != null);
                if (rule != null)
                {
                    tree.ExpandSubtree(jsonObj.ViewObject, rule.ExpandChildren.Value);
                }
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
    }
}
