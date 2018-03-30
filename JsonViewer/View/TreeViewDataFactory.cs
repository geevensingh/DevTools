namespace JsonViewer.View
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Windows.Controls;
    using JsonViewer.Model;

    internal static class TreeViewDataFactory
    {
        public static ObservableCollection<TreeViewData> CreateCollection(ListView tree, RootJsonObject rootObject)
        {
            return new ObservableCollection<TreeViewData>(CreateList(tree, rootObject));
        }

        public static TreeViewData CreateNode(ListView tree, JsonObject jsonObject)
        {
            var children = new List<TreeViewData>();
            foreach (JsonObject child in jsonObject.Children)
            {
                children.Add(CreateNode(tree, child));
            }

            return new TreeViewData(tree, jsonObject, children);
        }

        private static List<TreeViewData> CreateList(ListView tree, JsonObject jsonObject)
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
