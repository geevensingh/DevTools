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
        protected ConfigRule()
        {
            ExactKeys = new List<string>();
            ExactValues = new List<string>();
            ExactValueTypes = new List<string>();
            PartialKeys = new List<string>();
            PartialValues = new List<string>();
            PartialValueTypes = new List<string>();
            AppliesToParents = false;
            ForegroundBrush = null;
            BackgroundBrush = null;
            FontSize = null;
            ExpandChildren = null;
            WarningMessage = null;
            IgnoreCase = false;
        }

        public IList<string> ExactKeys { get; protected set; }

        public IList<string> ExactValues { get; protected set; }

        public IList<string> ExactValueTypes { get; protected set; }

        public IList<string> PartialKeys { get; protected set; }

        public IList<string> PartialValues { get; protected set; }

        public IList<string> PartialValueTypes { get; protected set; }

        public bool AppliesToParents { get; protected set; }

        public Brush ForegroundBrush { get; protected set; }

        public Brush BackgroundBrush { get; protected set; }

        public double? FontSize { get; protected set; }

        public int? ExpandChildren { get; protected set; }

        public string WarningMessage { get; protected set; }

        public bool IgnoreCase { get; protected set; }

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
            string key = this.NormalizeString(obj.Key);
            if (MatchStringToList(key, this.ExactKeys))
            {
                return true;
            }

            if (MatchPartialStringToList(key, this.PartialKeys))
            {
                return true;
            }

            string valueTypeString = this.NormalizeString(obj.ValueTypeString);
            if (MatchStringToList(valueTypeString, this.ExactValueTypes))
            {
                return true;
            }

            if (MatchPartialStringToList(valueTypeString, this.PartialValueTypes))
            {
                return true;
            }

            if (!obj.HasChildren || this.AppliesToParents)
            {
                string valueString = this.NormalizeString(obj.ValueString);
                if (MatchStringToList(valueString, this.ExactValues))
                {
                    return true;
                }

                if (MatchPartialStringToList(valueString, this.PartialValues))
                {
                    return true;
                }
            }

            return false;
        }

        private string NormalizeString(string str)
        {
            if (this.IgnoreCase)
            {
                return str.ToLower();
            }

            return str;
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
            IList<string> valueTypes = GetList(dict, "valueTypeIs", ignoreCase);
            IList<string> keyPartials = GetList(dict, "keyContains", ignoreCase);
            IList<string> valuePartials = GetList(dict, "valueContains", ignoreCase);
            IList<string> valueTypePartials = GetList(dict, "valueTypeContains", ignoreCase);

            Brush foregroundBrush = null;
            if (dict.ContainsKey("color"))
            {
                Color color = (Color)ColorConverter.ConvertFromString((string)dict["color"]);
                foregroundBrush = new SolidColorBrush(color);
            }

            Brush backgroundBrush = null;
            if (dict.ContainsKey("background"))
            {
                Color color = (Color)ColorConverter.ConvertFromString((string)dict["background"]);
                backgroundBrush = new SolidColorBrush(color);
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
                ExactValueTypes = valueTypes,
                PartialKeys = keyPartials,
                PartialValues = valuePartials,
                PartialValueTypes = valueTypePartials,
                AppliesToParents = appliesToParents,
                ForegroundBrush = foregroundBrush,
                BackgroundBrush = backgroundBrush,
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
