namespace JsonViewer.View
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using JsonViewer.Model;

    internal class RuleViewFactory
    {
        public static ObservableCollection<RuleView> CreateCollection()
        {
            List<RuleView> rules = new List<RuleView>();
            foreach (ConfigRule rule in Config.This.Rules)
            {
                rules.Add(new RuleView(rule));
            }

            return new ObservableCollection<RuleView>(rules);
        }
    }
}
