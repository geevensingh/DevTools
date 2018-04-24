namespace Json
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading.Tasks;
    using Utilities;

    public class RootObject : JsonObject
    {
        public RootObject()
            : base(string.Empty, new Dictionary<string, object>())
        {
        }

        public static async Task<RootObject> Create(Dictionary<string, object> jsonObj)
        {
            if (jsonObj == null)
            {
                return null;
            }

            return await Task.Run(
                () =>
                {
                    RootObject root = new RootObject();
                    var jsonObjects = new List<JsonObject>();
                    JsonObjectFactory.Flatten(ref jsonObjects, jsonObj, root);
                    return root;
                });
        }

        protected override void ApplyRules()
        {
            // Do nothing
        }

        private int ExpandLevelWithLessThanCount(int count)
        {
            int totalChildCount = this.TotalChildCount;
            int depth = 0;
            int lastCount = this.CountAtDepth(depth++);
            while (depth < count && lastCount < count && lastCount < totalChildCount)
            {
                lastCount += this.CountAtDepth(depth++);
            }

            if (lastCount > count)
            {
                return System.Math.Max(depth - 3, 0);
            }

            FileLogger.Assert(depth >= 2);
            return depth - 2;
        }
    }
}
