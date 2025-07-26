using Newtonsoft.Json;
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

        public float Weapons { get; set; }
        public float Health { get; set; }
        public float Class { get; set; }
        public float Grenade { get; set; }
        public float Super { get; set; }
        public float Melee { get; set; }

        [JsonIgnore]
        public float Sum => Weapons + Health + Class + Grenade + Super + Melee;

        public int Count { get; set; }

        public bool ConsiderUpgrading { get; set; }

        [JsonIgnore]
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
                this.Weapons == other.Weapons &&
                this.Health == other.Health &&
                this.Class == other.Class &&
                this.Grenade == other.Grenade &&
                this.Super == other.Super &&
                this.Melee == other.Melee;
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
