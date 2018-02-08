using System.Collections.Generic;

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
}
