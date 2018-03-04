namespace JsonViewer
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Windows.Media;
    using Utilities;

    internal class ConfigRule
    {
        public IList<string> ExactKeys { get; private set; }

        public IList<string> ExactValues { get; private set; }

        public IList<string> PartialKeys { get; private set; }

        public IList<string> PartialValues { get; private set; }

        public bool AppliesToParents { get; private set; }

        public Brush ForegroundBrush { get; private set; }

        public double? FontSize { get; private set; }

        public int? ExpandChildren { get; private set; }

        public string WarningMessage { get; private set; }

        public bool IgnoreCase { get; private set; }

        public static IList<ConfigRule> GenerateRules(ArrayList arrayList)
        {
            List<ConfigRule> rules = new List<ConfigRule>();
            foreach (Dictionary<string, object> dict in arrayList)
            {
                ConfigRule baseRule = GenerateRule(dict);
                rules.Add(baseRule);
            }

            return rules;
        }

        public bool Matches(JsonObject obj)
        {
            string key = obj.Key;
            if (this.IgnoreCase)
            {
                key = key.ToLower();
            }

            if (MatchStringToList(key, this.ExactKeys))
            {
                return true;
            }

            if (MatchPartialStringToList(key, this.PartialKeys))
            {
                return true;
            }

            if (obj.HasChildren && !this.AppliesToParents)
            {
                return false;
            }

            string valueString = obj.ValueString;
            if (this.IgnoreCase)
            {
                valueString = valueString.ToLower();
            }

            if (MatchStringToList(valueString, this.ExactValues))
            {
                return true;
            }

            if (MatchPartialStringToList(valueString, this.PartialValues))
            {
                return true;
            }

            return false;
        }

        private static bool MatchStringToList(string value, IList<string> values)
        {
            return values.Any(x => value == x);
        }

        private static bool MatchPartialStringToList(string value, IList<string> values)
        {
            return values.Any(x => value.Contains(x));
        }

        private static ConfigRule GenerateRule(Dictionary<string, object> dict)
        {
            bool ignoreCase = true;
            if (dict.ContainsKey("ignoreCase"))
            {
                ignoreCase = (bool)dict["ignoreCase"];
            }

            IList<string> keys = GetList(dict, "keyIs", ignoreCase);
            IList<string> values = GetList(dict, "valueIs", ignoreCase);
            IList<string> keyPartials = GetList(dict, "keyContains", ignoreCase);
            IList<string> valuePartials = GetList(dict, "valueContains", ignoreCase);

            Brush foregroundBrush = null;
            if (dict.ContainsKey("color"))
            {
                Color color = (Color)ColorConverter.ConvertFromString((string)dict["color"]);
                foregroundBrush = new SolidColorBrush(color);
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

            return new ConfigRule()
            {
                ExactKeys = keys,
                ExactValues = values,
                PartialKeys = keyPartials,
                PartialValues = valuePartials,
                AppliesToParents = appliesToParents,
                ForegroundBrush = foregroundBrush,
                FontSize = fontSize,
                ExpandChildren = expandChildren,
                WarningMessage = warningMessage,
                IgnoreCase = ignoreCase
            };
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
