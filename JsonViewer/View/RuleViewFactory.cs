namespace JsonViewer.View
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using JsonViewer.Model;

    internal class RuleViewFactory
    {
        public static ObservableCollection<RuleView> CreateCollection(RuleSet ruleSet)
        {
            List<RuleView> ruleViews = new List<RuleView>();
            IList<ConfigRule> rules = Config.This.Rules;
            for (int ii = 0; ii < rules.Count; ii++)
            {
                ruleViews.Add(new RuleView(rules[ii], ii, ruleSet));
            }

            return new ObservableCollection<RuleView>(ruleViews);
        }
    }
}
