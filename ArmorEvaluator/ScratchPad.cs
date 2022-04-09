using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArmorEvaluator
{
    internal record ScratchPad : IEquatable<ScratchPad>
    {
        private static HashSet<string> ScratchPads = new HashSet<string>();

        public ScratchPad(Item item, HashSet<AppliedWeightSet> appliedWeightSets)
        {
            Debug.Assert(!ScratchPads.Contains(item.Id));
            ScratchPads.Add(item.Id);

            Item = item;
            Weights = appliedWeightSets;
            NewTag = item.Tag;
        }

        public Item Item { get; set; }
        public HashSet<AppliedWeightSet> Weights { get; set; }

        public bool MeetsThreshold => Weights.Any(x => x.MeetsThreshold);

        public float AbsoluteValue => Weights.Sum(x => (x.Sum - x.WeightSet.Threshold) / x.WeightSet.Threshold);

        public string NewTag { get; private set; }

        public string NewTagReason { get; private set; }

        public bool TagChanged => NewTag != Item.Tag;

        public void SetTag(string newTag, string reason)
        {
            this.NewTag = newTag;
            this.NewTagReason = $"{newTag} -> {reason}";
        }

        public bool IsJunk => NewTag == "junk";

        //public bool Equals(ScratchPad? other)
        //{
        //    if (ReferenceEquals(null, other)) return false;
        //    if (ReferenceEquals(this, other)) return true;
        //    return this.Item.Equals(other.Item);
        //}
    }
}
