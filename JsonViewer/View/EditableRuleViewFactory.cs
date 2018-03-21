namespace JsonViewer.View
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using JsonViewer.Model;

    internal class EditableRuleViewFactory
    {
        public static ObservableCollection<EditableRuleView> CreateCollection(EditableRuleSet ruleSet)
        {
            List<EditableRuleView> ruleViews = new List<EditableRuleView>();
            IList<ConfigRule> rules = Config.Values.Rules;
            for (int ii = 0; ii < rules.Count; ii++)
            {
                ruleViews.Add(new EditableRuleView(rules[ii], ii, ruleSet));
            }

            return new ObservableCollection<EditableRuleView>(ruleViews);
        }
    }
}
