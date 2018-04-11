namespace JsonViewer.Model
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using System.Windows.Threading;
    using JsonViewer.View;
    using Utilities;

    public class Finder : NotifyPropertyChanged
    {
        private MainWindow _parentWindow;
        private RootObject _rootObject = null;
        private string _text = Properties.Settings.Default.FindText;
        private bool _shouldSearchKeys = Properties.Settings.Default.FindSearchKeys;
        private bool _shouldSearchValues = Properties.Settings.Default.FindSearchValues;
        private bool _shouldSearchParentValues = Properties.Settings.Default.FindSearchParentValues;
        private bool _shouldSearchValueTypes = Properties.Settings.Default.FindSearchValueTypes;
        private bool _shouldIgnoreCase = Properties.Settings.Default.FindIgnoreCase;
        private FindWindow _findWindow = null;
        private int _hitCount = 0;
        private List<JsonObject> _hits = new List<JsonObject>();
        private SingularAction _action = null;

        public Finder(MainWindow parentWindow)
        {
            _parentWindow = parentWindow;
            _action = new SingularAction(_parentWindow.Dispatcher);
        }

        public bool ShouldSearchKeys
        {
            get => _shouldSearchKeys;
            set
            {
                if (this.SetValue(ref _shouldSearchKeys, value, "ShouldSearchKeys"))
                {
                    _parentWindow.RunWhenever(
                        () =>
                        {
                            Properties.Settings.Default.FindSearchKeys = value;
                            Properties.Settings.Default.Save();
                        });

                    Update();
                }
            }
        }

        public bool ShouldSearchValues
        {
            get => _shouldSearchValues;
            set
            {
                if (this.SetValue(ref _shouldSearchValues, value, "ShouldSearchValues"))
                {
                    _parentWindow.RunWhenever(
                        () =>
                        {
                            Properties.Settings.Default.FindSearchValues = value;
                            Properties.Settings.Default.Save();
                        });

                    Update();
                }
            }
        }

        public bool ShouldSearchParentValues
        {
            get => _shouldSearchParentValues;
            set
            {
                if (this.SetValue(ref _shouldSearchParentValues, value, "ShouldSearchParentValues"))
                {
                    _parentWindow.RunWhenever(
                        () =>
                        {
                            Properties.Settings.Default.FindSearchParentValues = value;
                            Properties.Settings.Default.Save();
                        });

                    Update();
                }
            }
        }

        public bool ShouldSearchValueTypes
        {
            get => _shouldSearchValueTypes;
            set
            {
                if (this.SetValue(ref _shouldSearchValueTypes, value, "ShouldSearchValueTypes"))
                {
                    _parentWindow.RunWhenever(
                        () =>
                        {
                            Properties.Settings.Default.FindSearchValueTypes = value;
                            Properties.Settings.Default.Save();
                        });

                    Update();
                }
            }
        }

        public bool ShouldIgnoreCase
        {
            get => _shouldIgnoreCase;
            set
            {
                if (this.SetValue(ref _shouldIgnoreCase, value, "ShouldIgnoreCase"))
                {
                    _parentWindow.RunWhenever(
                        () =>
                        {
                            Properties.Settings.Default.FindIgnoreCase = value;
                            Properties.Settings.Default.Save();
                        });

                    Update();
                }
            }
        }

        public string Text
        {
            get => _text;
            set
            {
                if (this.SetValue(ref _text, value, "Text"))
                {
                    _parentWindow.RunWhenever(
                        () =>
                        {
                            Properties.Settings.Default.FindText = value;
                            Properties.Settings.Default.Save();
                        });

                    Update();
                }
            }
        }

        public int HitCount { get => _hitCount; }

        public bool HasWindow { get => _findWindow != null; }

        internal IList<JsonObject> Hits { get => _hits; }

        public void ShowWindow()
        {
            this.HideWindow();

            FileLogger.Assert(_findWindow == null);
            _findWindow = new FindWindow(_parentWindow, this);
            _findWindow.Closed += (sender, e) => this.HideWindow();

            _findWindow.Show();
            _findWindow.Closed += (sender, e) => this.HideWindow();
            this.FirePropertyChanged("HasWindow");
        }

        public void HideWindow()
        {
            if (_findWindow != null)
            {
                _findWindow.Close();
                _findWindow = null;
                this.FirePropertyChanged("HasWindow");
                _parentWindow.Focus();
            }
        }

        internal void SetObjects(RootObject rootObject)
        {
            if (_rootObject != null)
            {
                _rootObject.PropertyChanged -= OnRootObjectPropertyChanged;
            }

            _rootObject = rootObject;
            _rootObject.PropertyChanged += OnRootObjectPropertyChanged;
            Update();
        }

        private void OnRootObjectPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "AllChildren":
                    Update();
                    break;
            }
        }

        private void Update()
        {
            FindRule newRule = null;
            List<JsonObject> hits = new List<JsonObject>();
            List<JsonObject> misses = new List<JsonObject>();

            if (_rootObject != null && !string.IsNullOrEmpty(_text))
            {
                newRule = new FindRule(
                    text: _text,
                    ignoreCase: this.ShouldIgnoreCase,
                    searchKeys: this.ShouldSearchKeys,
                    searchValues: this.ShouldSearchValues,
                    searchValueTypes: this.ShouldSearchValueTypes,
                    appliesToParents: this.ShouldSearchParentValues,
                    matchType: MatchTypeEnum.Partial);

                foreach (JsonObject obj in _rootObject.AllChildren)
                {
                    bool matches = newRule.Matches(obj);
                    if (matches)
                    {
                        hits.Add(obj);
                    }
                    else
                    {
                        misses.Add(obj);
                    }
                }
            }
            else if (_rootObject != null)
            {
                misses = _rootObject.AllChildren;
            }

            this.SetValue(ref _hitCount, hits.Count, "HitCount");
            this.SetValueList(ref _hits, hits, "Hits");

            Func<Guid, SingularAction, Task<bool>> updateAction = new Func<Guid, SingularAction, Task<bool>>(async (actionId, action) =>
            {
                foreach (JsonObject obj in misses)
                {
                    obj.FindRule = null;
                    if (!await action.YieldAndContinue(actionId))
                    {
                        return false;
                    }
                }

                foreach (JsonObject obj in hits)
                {
                    obj.FindRule = newRule;
                    if (!await action.YieldAndContinue(actionId))
                    {
                        return false;
                    }
                }

                return true;
            });

            _action.BeginInvoke(DispatcherPriority.Background, updateAction);
        }
    }
}
