using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace JsonViewer
{
    class RootObject : JsonObject
    {
        IList<JsonObject> _children;
        public RootObject(IList<JsonObject> children) : base(string.Empty, string.Empty, null)
        {
            this._children = children;
        }
        internal override IList<JsonObject> Children { get => _children; }
    }

    internal class Finder
    {
        private Window _parentWindow;
        private RootObject _rootObject = null;
        private string _text = string.Empty;
        private bool _shouldSearchKeys = Properties.Settings.Default.FindSearchKeys;
        private bool _shouldSearchValues = Properties.Settings.Default.FindSearchValues;
        private bool _shouldSearchParentValues = Properties.Settings.Default.FindSearchParentValues;
        private bool _shouldIgnoreCase = Properties.Settings.Default.FindIgnoreCase;
        private FindWindow _findWindow = null;

        public Finder(Window parentWindow)
        {
            _parentWindow = parentWindow;
        }

        public void ShowWindow()
        {
            this.HideWindow();

            _findWindow = new FindWindow(_parentWindow, this);
            _findWindow.FindTextChanged += OnFindTextChanged;
            _findWindow.FindOptionsChanged += OnFindOptionsChanged;
            _findWindow.Show();
        }

        public void HideWindow()
        {
            if (_findWindow != null)
            {
                //_findWindow.FindTextChanged -= _textCa
                _findWindow.Close();
                _findWindow = null;
            }
        }

        public bool ShouldSearchKeys { get => _shouldSearchKeys; }
        public bool ShouldSearchValues { get => _shouldSearchValues; }
        public bool ShouldSearchParentValues { get => _shouldSearchParentValues; }
        public bool ShouldIgnoreCase { get => _shouldIgnoreCase; }
        public string Text { get => _text; }

        public void SetObjects(IList<JsonObject> jsonObjects)
        {
            _rootObject = new RootObject(jsonObjects);
        }

        private void OnFindOptionsChanged()
        {
            _shouldSearchKeys = _findWindow.ShouldSearchKeys;
            _shouldSearchValues = _findWindow.ShouldSearchValues;
            _shouldSearchParentValues = _findWindow.ShouldSearchParentValues;
            _shouldIgnoreCase = _findWindow.ShouldIgnoreCase;
            Update();
        }

        private void OnFindTextChanged(string oldText, string newText)
        {
            _text = newText;
            Update();
        }

        private void Update()
        {
            Highlight(_rootObject);
        }

        private void Highlight(JsonObject obj)
        {
            bool found = false;
            if (!string.IsNullOrEmpty(_text))
            {
                bool shouldSearchValue = obj.HasChildren ? this._shouldSearchParentValues : this._shouldSearchValues;
                if (this._shouldSearchKeys && this.CompareStrings(obj.Key, _text))
                {
                    found = true;
                }
                else if (obj.HasChildren ? this._shouldSearchParentValues : this._shouldSearchValues)
                {
                    found = this.CompareStrings(obj.ValueString, _text);
                }
            }
            obj.IsFindMatch = found;

            if (obj.HasChildren)
            {
                foreach (JsonObject child in obj.Children)
                {
                    this.Highlight(child);
                }
            }
        }

        private bool CompareStrings(string text, string substring)
        {
            if (this._shouldIgnoreCase)
            {
                return text.ToLower().Contains(substring.ToLower());
            }
            return text.Contains(substring);
        }
    }
}
