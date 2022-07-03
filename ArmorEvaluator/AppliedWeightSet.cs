using System;
using System.Collections.Generic;
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
        public bool MeetsThresholdOrIsSpecial => MeetsThreshold || Item.IsSpecial;

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
    }
}
