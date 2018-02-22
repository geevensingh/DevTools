namespace JsonViewer
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Windows.Media;
    using Utilities;

    internal class ConfigRule
    {
        private IList<string> _keys;
        private IList<string> _values;
        private IList<string> _keyPartials;
        private IList<string> _valuePartials;
        private Brush _foregroundBrush;
        private double? _fontSize;
        private bool _appliesToParents;

        public ConfigRule(IList<string> keys, IList<string> values, IList<string> keyPartials, IList<string> valuePartials, Brush foregroundBrush, double? fontSize, bool appliesToParents)
        {
            Debug.Assert(keys.Count + values.Count + keyPartials.Count + valuePartials.Count > 0, "No criteria found.");

            this._keys = keys;
            this._values = values;
            this._keyPartials = keyPartials;
            this._valuePartials = valuePartials;
            this._foregroundBrush = foregroundBrush;
            this._fontSize = fontSize;
            _appliesToParents = appliesToParents;
        }

        public Brush ForegroundBrush { get => _foregroundBrush; }

        public double? FontSize { get => _fontSize; }

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
            if (MatchStringToList(obj.Key, _keys))
            {
                return true;
            }

            if (MatchPartialStringToList(obj.Key, _keyPartials))
            {
                return true;
            }

            if (obj.HasChildren && !_appliesToParents)
            {
                return false;
            }

            if (MatchStringToList(obj.ValueString, _values))
            {
                return true;
            }

            if (MatchPartialStringToList(obj.ValueString, _valuePartials))
            {
                return true;
            }

            return false;
        }

        private static bool MatchStringToList(string value, IList<string> values)
        {
            value = value.ToLower();
            foreach (string v in values)
            {
                if (value == v)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchPartialStringToList(string value, IList<string> values)
        {
            value = value.ToLower();
            foreach (string v in values)
            {
                if (value.Contains(v))
                {
                    return true;
                }
            }

            return false;
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

            return new ConfigRule(keys, values, keyPartials, valuePartials, foregroundBrush, fontSize, appliesToParents);
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
