using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace JsonViewer
{
    class TreeViewDataFactory
    {
        public static ObservableCollection<TreeViewData> CreateCollection(RootObject rootObject)
        {

            return new ObservableCollection<TreeViewData>(CreateList(rootObject));
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

        public static TreeViewData CreateNode(JsonObject jsonObject)
        {
            var children = new List<TreeViewData>();
            foreach (JsonObject child in jsonObject.Children)
            {
                children.Add(CreateNode(child));
            }
            return new TreeViewData(jsonObject, children);
        }

    }
}
