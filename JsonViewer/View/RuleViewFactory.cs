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
            List<RuleView> ruleViews = new List<RuleView>();
            IList<ConfigRule> rules = Config.This.Rules;
            for (int ii = 0; ii < rules.Count; ii++)
            {
                ruleViews.Add(new RuleView(rules[ii], ii));
            }

            return new ObservableCollection<RuleView>(ruleViews);
        }
    }
}
