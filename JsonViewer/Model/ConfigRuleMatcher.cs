namespace JsonViewer.Model
{
    using System.Collections.Generic;
    using System.Linq;

    internal class ConfigRuleMatcher
    {
        private ConfigRule _rule;
        private SubMatcher _keyMatcher;
        private SubMatcher _valueTypeMatcher;
        private SubMatcher _valueMatcher;
        private Dictionary<JsonObject, bool> _lookup = new Dictionary<JsonObject, bool>();

        public ConfigRuleMatcher(ConfigRule rule)
        {
            _rule = rule;
            _keyMatcher = new SubMatcher(rule, rule.ExactKeys, rule.PartialKeys);
            _valueTypeMatcher = new SubMatcher(rule, rule.ExactValueTypes, rule.PartialValueTypes);
            _valueMatcher = new SubMatcher(rule, rule.ExactValues, rule.PartialValues);
        }

        public bool Matches(JsonObject obj)
        {
            if (!_lookup.ContainsKey(obj))
            {
                bool result = _keyMatcher.Matches(obj.Key) || _valueTypeMatcher.Matches(obj.ValueTypeString);
                if (!obj.HasChildren || _rule.AppliesToParents)
                {
                    result = result || _valueMatcher.Matches(obj.ValueString);
                }

                _lookup[obj] = result;
            }

            return _lookup[obj];
        }

        private class SubMatcher
        {
            private ConfigRule _rule;
            private IList<string> _exactMatches;
            private IList<string> _partialMatches;
            private Dictionary<string, bool> _lookup = new Dictionary<string, bool>();

            public SubMatcher(ConfigRule rule, IList<string> exactMatches, IList<string> partialMatches)
            {
                _rule = rule;
                _exactMatches = exactMatches;
                _partialMatches = partialMatches;
            }

            public bool Matches(string str)
            {
                str = this.NormalizeString(str);
                if (!_lookup.ContainsKey(str))
                {
                    _lookup[str] = _exactMatches.Any(x => str == x) || _partialMatches.Any(x => str.Contains(x));
                }

                return _lookup[str];
            }

            private string NormalizeString(string str)
            {
                return _rule.IgnoreCase ? str.ToLower() : str;
            }
        }
    }
}
