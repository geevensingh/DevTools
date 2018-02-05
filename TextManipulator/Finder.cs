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
            Highlight(_rootObject);
        }

        private void OnFindTextChanged(string oldText, string newText)
        {
            Highlight(_rootObject);
        }

        private void Highlight(JsonObject obj)
        {
            string substring = _findWindow.Text;
            bool found = false;
            if (!string.IsNullOrEmpty(substring))
            {
                bool shouldSearchValue = obj.HasChildren ? this._findWindow.ShouldSearchParentValues : this._findWindow.ShouldSearchValues;
                if (this._findWindow.ShouldSearchKeys && this.CompareStrings(obj.Key, substring))
                {
                    found = true;
                }
                else if (obj.HasChildren ? this._findWindow.ShouldSearchParentValues : this._findWindow.ShouldSearchValues)
                {
                    found = this.CompareStrings(obj.ValueString, substring);
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
            if (this._findWindow.ShouldIgnoreCase)
            {
                return text.ToLower().Contains(substring.ToLower());
            }
            return text.Contains(substring);
        }
    }
}
