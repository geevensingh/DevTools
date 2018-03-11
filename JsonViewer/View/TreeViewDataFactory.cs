namespace JsonViewer.View
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using JsonViewer.Model;

    internal static class TreeViewDataFactory
    {
        public static ObservableCollection<TreeViewData> CreateCollection(CustomTreeView tree, RootObject rootObject)
        {
            return new ObservableCollection<TreeViewData>(CreateList(tree, rootObject));
        }

        public static TreeViewData CreateNode(CustomTreeView tree, JsonObject jsonObject)
        {
            var children = new List<TreeViewData>();
            foreach (JsonObject child in jsonObject.Children)
            {
                children.Add(CreateNode(tree, child));
            }

            return new TreeViewData(tree, jsonObject, children);
        }

        private static List<TreeViewData> CreateList(CustomTreeView tree, JsonObject jsonObject)
        {
            var result = new List<TreeViewData>();
            foreach (JsonObject jsonChildren in jsonObject.Children)
            {
                result.Add(CreateNode(tree, jsonChildren));
            }

            return result;
        }
    }
}
