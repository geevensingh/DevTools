namespace JsonViewer.Model
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Linq;
    using JsonViewer.View;
    using Utilities;

    public class RuleSet : NotifyPropertyChanged
    {
        private ObservableCollection<RuleView> _rules;
        private bool _isDirty = false;

        public RuleSet()
        {
        }

        public ObservableCollection<RuleView> Rules
        {
            get
            {
                if (_rules == null)
                {
                    this.SetRules(RuleViewFactory.CreateCollection(this));
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

        internal void AddNewRule(RuleView newRuleView)
        {
            newRuleView.RuleSet = this;
            newRuleView.Index = _rules.Max((rule) => rule.Index) + 1;

            this.SetValue(ref _isDirty, true, "IsDirty");
        }

        internal int SetIndex(RuleView ruleView, int newIndex)
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
            foreach (RuleView ruleView in this.Rules)
            {
                configRules.Add(ruleView.Rule);
            }

            Config.This.Rules = configRules;

            if (Config.Save())
            {
                foreach (RuleView ruleView in this.Rules)
                {
                    ruleView.IsDirty = false;
                }

                this.SetValue(ref _isDirty, false, "IsDirty");
            }
        }

        internal void Discard()
        {
            this.SetValue(ref _isDirty, false, "IsDirty");
            Config.Reload();
            this.SetRules(RuleViewFactory.CreateCollection(this));
            this.FirePropertyChanged("IsDirty");
        }

        private void OnRulesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Remove:
                    foreach (RuleView ruleView in e.OldItems)
                    {
                        ruleView.Index = -1;
                        this.SetValue(ref _isDirty, true, "IsDirty");
                    }

                    break;
                case NotifyCollectionChangedAction.Move:
                    Debug.Assert(e.OldItems.Count == 1);
                    Debug.Assert(e.NewItems.Count == 1);
                    Debug.Assert(e.OldItems[0] == e.NewItems[0]);
                    break;
                case NotifyCollectionChangedAction.Add:
                    Debug.Assert(e.NewItems.Count == 1);
                    RuleView newRuleView = (RuleView)e.NewItems[0];
                    newRuleView.RuleSet = this;
                    newRuleView.UpdateIndex(e.NewStartingIndex);
                    this.SetValue(ref _isDirty, true, "IsDirty");
                    break;
                case NotifyCollectionChangedAction.Replace:
                case NotifyCollectionChangedAction.Reset:
                default:
                    Debug.Assert(false);
                    break;
            }
        }

        private void SetRules(ObservableCollection<RuleView> rules)
        {
            if (rules == _rules)
            {
                return;
            }

            if (_rules != null)
            {
                _rules.CollectionChanged -= OnRulesCollectionChanged;
                foreach (RuleView ruleView in _rules)
                {
                    ruleView.PropertyChanged -= OnRulePropertyChanged;
                }
            }

            if (rules != null)
            {
                rules.CollectionChanged += OnRulesCollectionChanged;
                foreach (RuleView ruleView in rules)
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
