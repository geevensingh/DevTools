namespace JsonViewer.Model
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows.Controls;
    using JsonViewer.View;
    using Utilities;

    public class RuleSet : NotifyPropertyChanged
    {
        private ObservableCollection<RuleView> _rules;
        private bool _isDirty = false;
        bool _isReordering = false;

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

        private void OnRulesCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Debug.Assert(e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Move);
            Debug.Assert(e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Replace);
            Debug.Assert(e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Reset);
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
            {
                foreach (RuleView foo in e.OldItems)
                {
                    foo.Index = -1;
                    this.SetValue(ref _isDirty, true, "IsDirty");
                }
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

        internal void Refresh()
        {
            this.SetRules(null);
        }

        internal void AddNewRule(InitializingNewItemEventArgs e)
        {
            RuleView newRuleView = (RuleView)e.NewItem;
            newRuleView.RuleSet = this;
            newRuleView.Index = _rules.Max((rule) => rule.Index) + 1;

            this.SetValue(ref _isDirty, true, "IsDirty");
        }

        internal void SetIndex(RuleView ruleView, int newIndex)
        {
            Debug.Assert(_isReordering == false);

            _isReordering = true;
            if (newIndex >= _rules.Count)
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

            _isReordering = false;
        }

        internal void Save()
        {
            this.SetValue(ref _isDirty, false, "IsDirty");
            foreach (RuleView ruleView in this.Rules)
            {
                ruleView.IsDirty = false;
            }
        }

        internal void Discard()
        {
            this.SetValue(ref _isDirty, false, "IsDirty");
            Config.This.ReloadRules();
            this.SetRules(RuleViewFactory.CreateCollection(this));
            this.FirePropertyChanged("IsDirty");
        }
    }
}
