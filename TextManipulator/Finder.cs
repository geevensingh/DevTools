using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TextManipulator
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

    class Finder
    {
        RootObject _rootObject = null;
        private FindWindow _findWindow;

        public Finder(FindWindow findWindow)
        {
            _findWindow = findWindow;
            _findWindow.FindTextChanged += OnFindTextChanged;
            _findWindow.FindOptionsChanged += OnFindOptionsChanged;
        }

        public void SetObjects(IList<JsonObject> jsonObjects)
        {
            _rootObject = new RootObject(jsonObjects);
        }

        private void OnFindOptionsChanged()
        {
            Highlight(_findWindow.textBox.Text, _rootObject);
        }

        private void OnFindTextChanged(string oldText, string newText)
        {
            if (!string.IsNullOrEmpty(newText))
            {
                Highlight(newText, _rootObject);
            }
        }

        private void Highlight(string substring, JsonObject obj)
        {
            bool found = false;
            if (this._findWindow.ShouldSearchKeys && this.CompareStrings(obj.Key, substring))
            {
                found = true;
            }
            else if (!obj.HasChildren && this._findWindow.ShouldSearchValues && this.CompareStrings(obj.ValueString, substring))
            {
                found = true;
            }
            obj.IsFindMatch = found;
            if (obj.HasChildren)
            {
                foreach (JsonObject child in obj.Children)
                {
                    this.Highlight(substring, child);
                }
            }
        }

        private bool CompareStrings(string text, string substring)
        {
            if (this._findWindow.ShouldIgnoreCase)
            {
                return text.ToLower().Contains(substring.ToLower());
            }
            return text.Contains(substring);
        }
    }
}
