namespace JsonViewer
{
    using System.Collections.Generic;

    internal class FindRule : ConfigRule
    {
        internal FindRule(string text, bool ignoreCase, bool searchKeys, bool searchValues, bool appliesToParents)
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

            PartialKeys = keys;
            PartialValues = values;
            ForegroundBrush = Config.This.GetBrush(ConfigValue.TreeViewSearchResultForeground);
            BackgroundBrush = Config.This.GetBrush(ConfigValue.TreeViewSearchResultBackground);
            FontSize = 18;
            IgnoreCase = ignoreCase;
            AppliesToParents = appliesToParents;
        }
    }
}
