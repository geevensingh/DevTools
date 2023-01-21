using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArmorEvaluator
{
    public record AppliedWeightSet
    {
        public WeightSet WeightSet { get; private set; }
        public Item Item { get; private set; }

        public float AdjustedThreshold
        {
            get
            {
                if (Item.Tier == "Exotic")
                {
                    return WeightSet.Threshold.GetApplicableThreshold(Item) * 0.8f;
                }

                return WeightSet.Threshold.GetApplicableThreshold(Item);
            }
        }
        public bool MeetsThreshold => (Sum >= AdjustedThreshold);

        public float Mobility => WeightSet.Mobility * Item.Mobility;
        public float Resilience => WeightSet.Resilience * Item.Resilience;
        public float Recovery => WeightSet.Recovery * Item.Recovery;
        public float Discipline => WeightSet.Discipline * Item.Discipline;
        public float Intellect => WeightSet.Intellect * Item.Intellect;
        public float Strength => WeightSet.Strength * Item.Strength;
        public float[] AllStats => new float[] { Mobility, Resilience, Recovery, Discipline, Intellect, Strength };
        public float Sum => AllStats.Sum();

        public static AppliedWeightSet Create(Item item, WeightSet weightSet)
        {
            return new AppliedWeightSet()
            {
                WeightSet = weightSet,
                Item = item,
            };
        }

        public static HashSet<AppliedWeightSet> Create(Item item, HashSet<WeightSet> weightSets)
        {
            return weightSets.Select(weightSet => AppliedWeightSet.Create(item, weightSet)).ToHashSet();
        }

        //public bool Equals(AppliedWeightSet? other)
        //{
        //    if (ReferenceEquals(null, other)) return false;
        //    if (ReferenceEquals(this, other)) return true;
        //    return this.Item.Equals(other.Item);
        //}

        public static bool IsLatterMuchBetter(IEnumerable<AppliedWeightSet> a, IEnumerable<AppliedWeightSet> b, bool isExotic)
        {
            float factor = isExotic ? 1.175f : 1.25f;
            Debug.Assert(a.Count() == b.Count());
            Debug.Assert(string.Join(", ", a.Select(x => x.WeightSet.Name).Distinct().OrderBy(x => x)) == string.Join(", ", b.Select(x => x.WeightSet.Name).Distinct().OrderBy(x => x)));

            foreach (var weightSetA in a)
            {
                var weightSetB = b.Single(x => x.WeightSet.Name == weightSetA.WeightSet.Name);
                if (weightSetA.Sum * factor > weightSetB.Sum)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
