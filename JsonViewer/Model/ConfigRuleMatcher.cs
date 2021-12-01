namespace JsonViewer.Model
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    internal class ConfigRuleMatcher
    {
        private ConfigRule _rule;
        private Dictionary<JsonObject, bool> _lookup = new Dictionary<JsonObject, bool>();

        public ConfigRuleMatcher(ConfigRule rule)
        {
            _rule = rule;
        }

        public bool Matches(JsonObject obj)
        {
            if (!_lookup.ContainsKey(obj))
            {
                bool result = false;
                if (_rule.MatchField != MatchFieldEnum.Value || !obj.HasChildren || _rule.AppliesToParents)
                {
                    string field = this.GetField(obj);
                    if (_rule.MatchType == MatchTypeEnum.Exact)
                    {
                        result = _rule.String == field;
                    }
                    else
                    {
                        FileLogger.Assert(_rule.MatchType == MatchTypeEnum.Partial);
                        result = field.Contains(_rule.String);
                    }
                }

                _lookup[obj] = result;
            }

            return _lookup[obj];
        }

        private string GetField(JsonObject obj)
        {
            string str;
            switch (_rule.MatchField)
            {
                case MatchFieldEnum.Key:
                    str = obj.Key;
                    break;
                case MatchFieldEnum.Type:
                    str = obj.ValueTypeString;
                    break;
                case MatchFieldEnum.Value:
                    str = obj.ValueString;
                    break;
                default:
                    Debug.Fail("Unknown MatchField");
                    throw new System.ArgumentException("Unknown MatchField");
            }

            return this.NormalizeString(str);
        }

        private string NormalizeString(string str)
        {
            return _rule.IgnoreCase ? str.ToLower() : str;
        }
    }
}
