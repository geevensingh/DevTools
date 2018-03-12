namespace JsonViewer.Model
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Windows.Media;
    using Utilities;

    internal class FindRule : IRule
    {
        private List<ConfigRule> _rules = new List<ConfigRule>();

        internal FindRule(string text, bool ignoreCase, bool searchKeys, bool searchValues, bool searchValueTypes, bool appliesToParents, MatchTypeEnum matchType)
        {
            string foregroundString = Config.This.GetColor(matchType == MatchTypeEnum.Exact ? ConfigValue.TreeViewSimilarForeground : ConfigValue.TreeViewSearchResultForeground).GetName();
            string backgroundString = Config.This.GetColor(matchType == MatchTypeEnum.Exact ? ConfigValue.TreeViewSimilarBackground : ConfigValue.TreeViewSearchResultBackground).GetName();

            void AddRule(MatchFieldEnum matchField)
            {
                _rules.Add(new ConfigRule()
                {
                    String = text,
                    MatchField = matchField,
                    MatchType = matchType,
                    IgnoreCase = ignoreCase,
                    ForegroundString = foregroundString,
                    BackgroundString = backgroundString
                });
            }

            if (searchKeys)
            {
                AddRule(MatchFieldEnum.Key);
            }

            if (searchValues)
            {
                AddRule(MatchFieldEnum.Value);
            }

            if (searchValueTypes)
            {
                AddRule(MatchFieldEnum.Type);
            }
        }

        public Brush ForegroundBrush => _rules[0].ForegroundBrush;

        public Brush BackgroundBrush => _rules[0].BackgroundBrush;

        public double? FontSize => null;

        public string WarningMessage => string.Empty;

        public int? ExpandChildren => null;

        public bool Matches(JsonObject obj)
        {
            return _rules.FirstOrDefault(x => x.Matches(obj)) != null;
        }
    }
}
