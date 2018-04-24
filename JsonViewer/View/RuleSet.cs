namespace JsonViewer.View
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Windows.Media;
    using JsonViewer.Model;
    using Utilities;

    public class RuleSet : NotifyPropertyChanged
    {
        private JsonObject _jsonObject = null;
        private FindRule _findRule = null;
        private FindRule _matchRule = null;
        private List<IRule> _rules = new List<IRule>();
        private Brush _foregroundBrush = Config.Values.GetBrush(ConfigValue.DefaultForeground);
        private Brush _backgroundBrush = Config.Values.GetBrush(ConfigValue.DefaultBackground);
        private double _fontSize = Config.Values.DefaultFontSize;
        private int? _expandChildren = null;
        private IEnumerable<string> _warningMessages = null;

        public RuleSet(JsonObject jsonObject)
        {
            Debug.Assert(System.Threading.Thread.CurrentThread.ManagedThreadId == 1);

            _jsonObject = jsonObject;
        }

        public FindRule FindRule { get => _findRule; set => this.SetFindRule(value); }

        public FindRule MatchRule { get => _matchRule; set => this.SetMatchRule(value); }

        public Brush TextColor { get => _foregroundBrush; }

        public Brush BackgroundColor { get => _backgroundBrush; }

        public double FontSize { get => _fontSize; }

        public int? ExpandChildren { get => _expandChildren; }

        public IEnumerable<string> WarningMessages { get => _warningMessages; }

        internal void SetFindRule(FindRule value)
        {
            FindRule oldRule = _findRule;
            FindRule newRule = value;

            //Debug.Assert(newRule == null || newRule.Matches(_jsonObject));

            if (oldRule == null && newRule == null)
            {
                return;
            }

            if (oldRule != null && newRule != null)
            {
                // Even though the old rule and the new rule are different,
                // let's treat them as the same and assume that they apply
                // the same formatting rules.
                return;
            }

            if (_findRule != null)
            {
                _rules.Remove(_findRule);
            }

            _findRule = newRule;

            if (newRule != null)
            {
                _rules.Insert(0, newRule);
            }

            this.FirePropertyChanged(new string[] { "FindRule", "Rules" });
            this.Update();
        }

        internal void SetMatchRule(FindRule value)
        {
            FindRule oldRule = _matchRule;
            FindRule newRule = value;

            //Debug.Assert(newRule == null || newRule.Matches(_jsonObject));

            if (oldRule == null && newRule == null)
            {
                return;
            }

            if (oldRule != null && newRule != null)
            {
                // Even though the old rule and the new rule are different,
                // let's treat them as the same and assume that they apply
                // the same formatting rules.
                return;
            }

            if (_matchRule != null)
            {
                _rules.Remove(_matchRule);
            }

            _matchRule = newRule;

            if (newRule != null)
            {
                _rules.Add(newRule);
            }

            this.FirePropertyChanged(new string[] { "MatchRule", "Rules" });
            this.Update();
        }

        internal void DismissWarningMessage()
        {
            _rules.RemoveAll((rule) => !string.IsNullOrEmpty(rule.WarningMessage));
        }

        internal void Initialize()
        {
            //List<IRule> newRules = new List<IRule>(Config.Values.Rules.Where(rule => rule.Matches(_jsonObject)));
            //if (_findRule != null)
            //{
            //    newRules.Insert(0, _findRule);
            //}

            //if (_matchRule != null)
            //{
            //    newRules.Add(_matchRule);
            //}

            //this.SetValueList(ref _rules, newRules, "Rules");
            this.Update();
        }

        private Brush CalculateForegroundBrush()
        {
            return _rules.FirstOrDefault((rule) => rule.ForegroundBrush != null)?.ForegroundBrush ?? Config.Values.GetBrush(ConfigValue.DefaultForeground);
        }

        private Brush CalculateBackgroundBrush()
        {
            return _rules.FirstOrDefault((rule) => rule.BackgroundBrush != null)?.BackgroundBrush ?? Config.Values.GetBrush(ConfigValue.DefaultBackground);
        }

        private double CalculateFontSize()
        {
            return _rules.FirstOrDefault((rule) => rule.FontSize.HasValue)?.FontSize.Value ?? Config.Values.DefaultFontSize;
        }

        private int? CalculateExpandChildren()
        {
            return _rules.Max((rule) => rule.ExpandChildren);
        }

        private IEnumerable<string> CalculateWarningMessages()
        {
            return _rules.Where(rule => !string.IsNullOrEmpty(rule.WarningMessage)).Select(rule => rule.WarningMessage);
        }

        private void Update()
        {
            this.SetValue(ref _fontSize, this.CalculateFontSize(), "FontSize");
            this.SetValue(ref _foregroundBrush, this.CalculateForegroundBrush(), "TextColor");
            this.SetValue(ref _backgroundBrush, this.CalculateBackgroundBrush(), "BackgroundColor");
            this.SetValue(ref _expandChildren, this.CalculateExpandChildren(), "ExpandChildren");
            this.SetValue(ref _warningMessages, this.CalculateWarningMessages(), "WarningMessages");
        }
    }
}
