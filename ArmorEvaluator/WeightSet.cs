using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArmorEvaluator
{
    public class WeightSet : IEquatable<WeightSet>
    {
        public string Name { get; set; }
        public Threshold Threshold { get; set; }

        public float Mobility { get; set; }
        public float Resilience { get; set; }
        public float Recovery { get; set; }
        public float Discipline { get; set; }
        public float Intellect { get; set; }
        public float Strength { get; set; }

        public float Sum => Mobility + Resilience + Recovery + Discipline + Intellect + Strength;

        public int Count { get; set; }

        public float OverallNormalizedThreshold => Threshold.Average / Sum;

        public float GetNormalizedThreshold(string itemType)
        {
            return Threshold.GetApplicableThreshold(itemType) / Sum;
        }

        public bool Equals(WeightSet? other)
        {
            if (other is null)
            {
                return false;
            }

            if (this.GetType() != other.GetType())
            {
                return false;
            }

            return this.Name == other.Name &&
                this.Mobility == other.Mobility &&
                this.Resilience == other.Resilience &&
                this.Recovery == other.Recovery &&
                this.Discipline == other.Discipline &&
                this.Intellect == other.Intellect &&
                this.Strength == other.Strength;
        }

        public override bool Equals(object obj)
        {
            return this.Equals(obj as WeightSet);
        }

        public static bool operator ==(WeightSet a, WeightSet b)
        {
            if (a is null)
            {
                return b is null;
            }

            return a.Equals(b);
        }

        public static bool operator !=(WeightSet a, WeightSet b)
        {
            return !(a == b);
        }
    }
}
