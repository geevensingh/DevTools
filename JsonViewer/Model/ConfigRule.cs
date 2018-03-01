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
        private IList<string> _keys;
        private IList<string> _values;
        private IList<string> _keyPartials;
        private IList<string> _valuePartials;
        private bool _appliesToParents;

        public ConfigRule(IList<string> keys, IList<string> values, IList<string> keyPartials, IList<string> valuePartials, bool appliesToParents)
        {
            Debug.Assert(keys.Count + values.Count + keyPartials.Count + valuePartials.Count > 0, "No criteria found.");

            this._keys = keys;
            this._values = values;
            this._keyPartials = keyPartials;
            this._valuePartials = valuePartials;
            _appliesToParents = appliesToParents;
        }

        public Brush ForegroundBrush { get; private set; }

        public double? FontSize { get; private set; }

        public int? ExpandChildren { get; private set; }

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
            string key = obj.Key.ToLower();
            if (MatchStringToList(key, _keys))
            {
                return true;
            }

            if (MatchPartialStringToList(key, _keyPartials))
            {
                return true;
            }

            if (obj.HasChildren && !_appliesToParents)
            {
                return false;
            }

            string valueString = obj.ValueString.ToLower();
            if (MatchStringToList(valueString, _values))
            {
                return true;
            }

            if (MatchPartialStringToList(valueString, _valuePartials))
            {
                return true;
            }

            return false;
        }

        private static bool MatchStringToList(string value, IList<string> values)
        {
            Debug.Assert(value == value.ToLower());
            Debug.Assert(values.All(x => x.ToLower() == x));
            return values.Any(x => value == x);
        }

        private static bool MatchPartialStringToList(string value, IList<string> values)
        {
            Debug.Assert(value == value.ToLower());
            Debug.Assert(values.All(x => x.ToLower() == x));
            return values.Any(x => value.Contains(x));
        }

        private static ConfigRule GenerateRule(Dictionary<string, object> dict)
        {
            IList<string> keys = GetList(dict, "keyIs");
            IList<string> values = GetList(dict, "valueIs");
            IList<string> keyPartials = GetList(dict, "keyContains");
            IList<string> valuePartials = GetList(dict, "valueContains");

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

            return new ConfigRule(keys, values, keyPartials, valuePartials, appliesToParents)
            {
                ForegroundBrush = foregroundBrush,
                FontSize = fontSize,
                ExpandChildren = expandChildren
            };
        }

        private static IList<string> GetList(Dictionary<string, object> dict, string key)
        {
            List<string> values = new List<string>();
            if (dict.ContainsKey(key))
            {
                ArrayList arrayList = (ArrayList)dict[key];
                foreach (string value in arrayList)
                {
                    values.Add(value.ToLower());
                }
            }

            return values;
        }
    }
}
