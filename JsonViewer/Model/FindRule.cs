namespace JsonViewer
{
    using System.Collections.Generic;

    internal class FindRule : ConfigRule
    {
        internal FindRule(string text, bool ignoreCase, bool searchKeys, bool searchValues, bool searchValueTypes, bool appliesToParents, bool exactMatch)
            : base()
        {
            text = ignoreCase ? text.ToLower() : text;

            List<string> keys = new List<string>();
            if (searchKeys)
            {
                keys.Add(text);
            }

            List<string> values = new List<string>();
            if (searchValues)
            {
                values.Add(text);
            }

            List<string> valueTypes = new List<string>();
            if (searchValueTypes)
            {
                valueTypes.Add(text);
            }

            if (exactMatch)
            {
                ExactKeys = keys;
                ExactValues = values;
                ExactValueTypes = valueTypes;
            }
            else
            {
                PartialKeys = keys;
                PartialValues = values;
                PartialValueTypes = valueTypes;
            }

            ForegroundBrush = Config.This.GetBrush(exactMatch ? ConfigValue.TreeViewSimilarForeground : ConfigValue.TreeViewSearchResultForeground);
            BackgroundBrush = Config.This.GetBrush(exactMatch ? ConfigValue.TreeViewSimilarBackground : ConfigValue.TreeViewSearchResultBackground);
            IgnoreCase = ignoreCase;
            AppliesToParents = appliesToParents;
        }
    }
}
