namespace JsonViewer
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Windows;

    public class Finder : NotifyPropertyChanged
    {
        private Window _parentWindow;
        private RootObject _rootObject = null;
        private string _text = Properties.Settings.Default.FindText;
        private bool _shouldSearchKeys = Properties.Settings.Default.FindSearchKeys;
        private bool _shouldSearchValues = Properties.Settings.Default.FindSearchValues;
        private bool _shouldSearchParentValues = Properties.Settings.Default.FindSearchParentValues;
        private bool _shouldIgnoreCase = Properties.Settings.Default.FindIgnoreCase;
        private FindWindow _findWindow = null;
        private int _hitCount = 0;
        private List<JsonObject> _hits = new List<JsonObject>();

        public Finder(Window parentWindow)
        {
            _parentWindow = parentWindow;
        }

        public bool ShouldSearchKeys
        {
            get => _shouldSearchKeys;
            set
            {
                if (this.SetValue(ref _shouldSearchKeys, value, "ShouldSearchKeys"))
                {
                    Properties.Settings.Default.FindSearchKeys = _shouldSearchKeys;
                    Properties.Settings.Default.Save();

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
                    Properties.Settings.Default.FindSearchValues = _shouldSearchValues;
                    Properties.Settings.Default.Save();

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
                    Properties.Settings.Default.FindSearchParentValues = _shouldSearchParentValues;
                    Properties.Settings.Default.Save();

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
                    Properties.Settings.Default.FindIgnoreCase = _shouldIgnoreCase;
                    Properties.Settings.Default.Save();

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
                    Properties.Settings.Default.FindText = _text;
                    Properties.Settings.Default.Save();

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

            Debug.Assert(_findWindow == null);
            _findWindow = new FindWindow(_parentWindow, this);
            _findWindow.Show();
            this.FirePropertyChanged("HasWindow");
        }

        public void HideWindow()
        {
            if (_findWindow != null)
            {
                _findWindow.Close();
                _findWindow = null;
                this.FirePropertyChanged("HasWindow");
            }
        }

        internal void SetObjects(RootObject rootObject)
        {
            _rootObject = rootObject;
            Update();
        }

        private void Update()
        {
            List<JsonObject> hits = new List<JsonObject>();
            if (_rootObject != null)
            {
                Highlight(_rootObject, ref hits);
            }

            if (this.SetValue(ref _hitCount, hits.Count, "HitCount"))
            {
                // If the hit count changed, then certainly the hits changed.
                this.SetValue(ref _hits, hits, "Hits");
                return;
            }

            // But if the hit count did NOT change, then we have to look at the lists
            Debug.Assert(hits.Count == _hits.Count);
            for (int ii = 0; ii < hits.Count; ii++)
            {
                if (hits[ii] != _hits[ii])
                {
                    this.SetValue(ref _hits, hits, "Hits");
                    return;
                }
            }
        }

        private void Highlight(JsonObject obj, ref List<JsonObject> hits)
        {
            bool found = false;
            if (!string.IsNullOrEmpty(_text))
            {
                if (_shouldSearchKeys && this.CompareStrings(obj.Key, _text))
                {
                    found = true;
                }
                else if (obj.HasChildren ? _shouldSearchParentValues : _shouldSearchValues)
                {
                    found = this.CompareStrings(obj.ValueString, _text);
                }
            }

            if (found)
            {
                hits.Add(obj);
            }

            if (obj.IsFindMatch != found)
            {
                obj.IsFindMatch = found;
            }

            if (obj.HasChildren)
            {
                foreach (JsonObject child in obj.Children)
                {
                    this.Highlight(child, ref hits);
                }
            }
        }

        private bool CompareStrings(string text, string substring)
        {
            if (_shouldIgnoreCase)
            {
                return text.ToLower().Contains(substring.ToLower());
            }

            return text.Contains(substring);
        }
    }
}
