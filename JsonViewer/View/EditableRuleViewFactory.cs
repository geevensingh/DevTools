namespace JsonViewer.View
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using JsonViewer.Model;

    internal class EditableRuleViewFactory
    {
        public static ObservableCollection<EditableRuleView> CreateCollection(EditableRuleSet ruleSet, ConfigValues configValues)
        {
            List<EditableRuleView> ruleViews = new List<EditableRuleView>();
            IList<ConfigRule> rules = configValues.Rules;
            for (int ii = 0; ii < rules.Count; ii++)
            {
                ruleViews.Add(new EditableRuleView(rules[ii], ii, ruleSet, configValues));
            }

            return new ObservableCollection<EditableRuleView>(ruleViews);
        }
    }
}
