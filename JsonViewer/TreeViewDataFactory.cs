namespace JsonViewer
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;

    internal class TreeViewDataFactory
    {
        public static ObservableCollection<TreeViewData> CreateCollection(RootObject rootObject)
        {
            return new ObservableCollection<TreeViewData>(CreateList(rootObject));
        }

        public static TreeViewData CreateNode(JsonObject jsonObject)
        {
            var children = new List<TreeViewData>();
            foreach (JsonObject child in jsonObject.Children)
            {
                children.Add(CreateNode(child));
            }

            return new TreeViewData(jsonObject, children);
        }

        private static List<TreeViewData> CreateList(JsonObject jsonObject)
        {
            var result = new List<TreeViewData>();
            foreach (JsonObject jsonChildren in jsonObject.Children)
            {
                result.Add(CreateNode(jsonChildren));
            }

            return result;
        }
    }
}
