namespace JsonViewer.Model
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Windows.Media;
    using Utilities;

    public class FindRule : IRule
    {
        private List<ConfigRule> _rules = new List<ConfigRule>();
        private Brush _foregroundBrush;
        private Brush _backgroundBrush;

        internal FindRule(string text, bool ignoreCase, bool searchKeys, bool searchValues, bool searchValueTypes, bool appliesToParents, MatchTypeEnum matchType)
        {
            System.Diagnostics.Debug.Assert(System.Threading.Thread.CurrentThread.ManagedThreadId == 1);

            _foregroundBrush = Config.Values.GetBrush(matchType == MatchTypeEnum.Exact ? ConfigValue.SimilarNodeForeground : ConfigValue.SearchResultForeground);
            _backgroundBrush = Config.Values.GetBrush(matchType == MatchTypeEnum.Exact ? ConfigValue.SimilarNodeBackground : ConfigValue.SearchResultBackground);

            if (ignoreCase)
            {
                text = text.ToLower();
            }

            void AddRule(MatchFieldEnum matchField)
            {
                _rules.Add(new ConfigRule()
                {
                    String = text,
                    MatchField = matchField,
                    MatchType = matchType,
                    IgnoreCase = ignoreCase,
                    AppliesToParents = appliesToParents
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

        public Brush ForegroundBrush => _foregroundBrush;

        public Brush BackgroundBrush => _backgroundBrush;

        public double? FontSize => null;

        public string WarningMessage => string.Empty;

        public int? ExpandChildren => null;

        public bool Matches(JsonObject obj)
        {
            return _rules.Any(x => x.Matches(obj));
        }
    }
}
