namespace JsonViewer.Model
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading.Tasks;
    using JsonViewer.View;
    using Utilities;

    public class EditableRuleSet : NotifyPropertyChanged
    {
        private ConfigValues _configValues;
        private ObservableCollection<EditableRuleView> _rules;
        private bool _isDirty = false;

        public EditableRuleSet(ConfigValues configValues)
        {
            _configValues = configValues;
        }

        public ObservableCollection<EditableRuleView> Rules
        {
            get
            {
                if (_rules == null)
                {
                    this.SetRules(EditableRuleViewFactory.CreateCollection(this, _configValues));
                }

                return _rules;
            }

            set
            {
                this.SetRules(value);
            }
        }

        public bool IsDirty { get => _isDirty || this.Rules.Any((rule) => rule.IsDirty); }

        internal void Refresh()
        {
            this.SetRules(null);
        }

        internal void AddNewRule(EditableRuleView newRuleView)
        {
            newRuleView.RuleSet = this;
            newRuleView.Index = _rules.Max((rule) => rule.Index) + 1;

            this.SetValue(ref _isDirty, true, "IsDirty");
        }

        internal int SetIndex(EditableRuleView ruleView, int newIndex)
        {
            if (newIndex < 0)
            {
                newIndex = 0;
            }
            else if (newIndex >= _rules.Count)
            {
                newIndex = _rules.Count - 1;
            }

            int oldIndex = _rules.IndexOf(ruleView);
            if (oldIndex != -1 && newIndex != -1)
            {
                _rules.Move(oldIndex, newIndex);
            }

            for (int ii = 0; ii < _rules.Count; ii++)
            {
                _rules[ii].UpdateIndex(ii);
            }

            return newIndex;
        }

        internal void Save()
        {
            List<ConfigRule> configRules = new List<ConfigRule>();
            foreach (EditableRuleView ruleView in this.Rules)
            {
                configRules.Add(ruleView.Rule);
            }

            _configValues.Rules = configRules;
            this.SetValue(ref _isDirty, false, "IsDirty");
        }

        private void OnRulesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Remove:
                    foreach (EditableRuleView ruleView in e.OldItems)
                    {
                        ruleView.Index = -1;
                        this.SetValue(ref _isDirty, true, "IsDirty");
                    }

                    break;
                case NotifyCollectionChangedAction.Move:
                    FileLogger.Assert(e.OldItems.Count == 1);
                    FileLogger.Assert(e.NewItems.Count == 1);
                    FileLogger.Assert(e.OldItems[0] == e.NewItems[0]);
                    break;
                case NotifyCollectionChangedAction.Add:
                    FileLogger.Assert(e.NewItems.Count == 1);
                    EditableRuleView newRuleView = (EditableRuleView)e.NewItems[0];
                    newRuleView.RuleSet = this;
                    newRuleView.UpdateIndex(e.NewStartingIndex);
                    newRuleView.SetConfigValues(_configValues);
                    this.SetValue(ref _isDirty, true, "IsDirty");
                    break;
                case NotifyCollectionChangedAction.Replace:
                case NotifyCollectionChangedAction.Reset:
                default:
                    FileLogger.Assert(false);
                    break;
            }
        }

        private void SetRules(ObservableCollection<EditableRuleView> rules)
        {
            if (rules == _rules)
            {
                return;
            }

            if (_rules != null)
            {
                _rules.CollectionChanged -= OnRulesCollectionChanged;
                foreach (EditableRuleView ruleView in _rules)
                {
                    ruleView.PropertyChanged -= OnRulePropertyChanged;
                }
            }

            if (rules != null)
            {
                rules.CollectionChanged += OnRulesCollectionChanged;
                foreach (EditableRuleView ruleView in rules)
                {
                    ruleView.PropertyChanged += OnRulePropertyChanged;
                }
            }

            _rules = rules;
            this.FirePropertyChanged("Rules");
        }

        private void OnRulePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "IsDirty":
                    this.FirePropertyChanged(e.PropertyName);
                    break;
            }
        }
    }
}
