namespace JsonViewer.Model
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows.Media;
    using Utilities;

    internal static class ConfigRuleFactory
    {
        public static IList<ConfigRule> GenerateRules(ArrayList arrayList)
        {
            List<ConfigRule> rules = new List<ConfigRule>();
            foreach (Dictionary<string, object> dict in arrayList)
            {
                rules.AddRange(GenerateRules(dict));
            }

            return rules;
        }

        private static IList<ConfigRule> GenerateRules(Dictionary<string, object> dict)
        {
            bool ignoreCase = true;
            if (dict.ContainsKey("ignoreCase"))
            {
                ignoreCase = (bool)dict["ignoreCase"];
            }

            string foregroundString = null;
            if (dict.ContainsKey("color"))
            {

                foregroundString = (string)dict["color"];
            }

            string backgroundString = null;
            if (dict.ContainsKey("background"))
            {
                backgroundString = (string)dict["background"];
            }

            double? fontSize = null;
            if (dict.ContainsKey("fontSize"))
            {
                fontSize = Converters.ToDouble(dict["fontSize"]);
            }

            bool appliesToParents = false;
            if (dict.ContainsKey("appliesToParents"))
            {
                appliesToParents = (bool)dict["appliesToParents"];
            }

            int? expandChildren = null;
            if (dict.ContainsKey("expandChildren"))
            {
                expandChildren = (int)dict["expandChildren"];
            }

            string warningMessage = null;
            if (dict.ContainsKey("warningMessage"))
            {
                warningMessage = (string)dict["warningMessage"];
            }

            List<ConfigRule> rules = new List<ConfigRule>();
            foreach (string key in GetList(dict, "keyIs", ignoreCase))
            {
                rules.Add(MakeRule(key, MatchTypeEnum.Exact, MatchFieldEnum.Key));
            }

            foreach (string key in GetList(dict, "keyContains", ignoreCase))
            {
                rules.Add(MakeRule(key, MatchTypeEnum.Partial, MatchFieldEnum.Key));
            }

            foreach (string value in GetList(dict, "valueIs", ignoreCase))
            {
                rules.Add(MakeRule(value, MatchTypeEnum.Exact, MatchFieldEnum.Value));
            }

            foreach (string value in GetList(dict, "valueContains", ignoreCase))
            {
                rules.Add(MakeRule(value, MatchTypeEnum.Partial, MatchFieldEnum.Value));
            }

            foreach (string valueType in GetList(dict, "valueTypeIs", ignoreCase))
            {
                rules.Add(MakeRule(valueType, MatchTypeEnum.Exact, MatchFieldEnum.Type));
            }

            foreach (string valueType in GetList(dict, "valueTypeContains", ignoreCase))
            {
                rules.Add(MakeRule(valueType, MatchTypeEnum.Partial, MatchFieldEnum.Type));
            }

            Debug.Assert(rules.Count > 0);
            return rules;

            ConfigRule MakeRule(string str, MatchTypeEnum matchType, MatchFieldEnum matchField)
            {
                return new ConfigRule()
                {
                    String = str,
                    MatchType = matchType,
                    MatchField = matchField,
                    IgnoreCase = ignoreCase,
                    ForegroundString = foregroundString,
                    BackgroundString = backgroundString,
                    FontSize = fontSize,
                    AppliesToParents = appliesToParents,
                    ExpandChildren = expandChildren,
                    WarningMessage = warningMessage
                };
            }
        }

        private static IList<string> GetList(Dictionary<string, object> dict, string key, bool ignoreCase)
        {
            List<string> values = new List<string>();
            if (dict.ContainsKey(key))
            {
                ArrayList arrayList = (ArrayList)dict[key];
                foreach (string value in arrayList)
                {
                    values.Add(ignoreCase ? value.ToLower() : value);
                }
            }

            return values;
        }
    }
}
