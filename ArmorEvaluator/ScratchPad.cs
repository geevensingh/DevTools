using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArmorEvaluator
{
    internal record ScratchPad : IEquatable<ScratchPad>
    {
        public Item Item { get; set; }
        public HashSet<AppliedWeightSet> Weights { get; set; }

        public bool MeetsThreshold => Weights.Any(x => x.MeetsThreshold);

        public float AbsoluteValue => Weights.Sum(x => (x.Sum - x.WeightSet.Threshold) / x.WeightSet.Threshold);

        //public bool Equals(ScratchPad? other)
        //{
        //    if (ReferenceEquals(null, other)) return false;
        //    if (ReferenceEquals(this, other)) return true;
        //    return this.Item.Equals(other.Item);
        //}
    }
}
