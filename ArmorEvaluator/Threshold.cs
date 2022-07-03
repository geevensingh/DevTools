using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArmorEvaluator
{
    public class Threshold
    {
        public float Helmet { get; set; } = 1f;
        public float Gauntlets { get; set; } = 1f;
        public float ChestArmor { get; set; } = 1f;
        public float LegArmor { get; set; } = 1f;

        public float GetApplicableThreshold(Item item)
        {
            if (item.IsClassItem)
            {
                return 1f;
            }

            switch (item.Type)
            {
                case "Helmet":
                    return this.Helmet;
                case "Gauntlets":
                    return this.Gauntlets;
                case "Chest Armor":
                    return this.ChestArmor;
                case "Leg Armor":
                    return this.LegArmor;
                default:
                    throw new ArgumentOutOfRangeException(nameof(item), $"Unknown type {item.Type}");
            }
        }

        internal bool Set(string itemType, float newThreshold)
        {
            bool result;
            switch (itemType)
            {
                case "Helmet":
                    result = this.Helmet != newThreshold;
                    this.Helmet = newThreshold;
                    break;
                case "Gauntlets":
                    result = this.Gauntlets != newThreshold;
                    this.Gauntlets = newThreshold;
                    break;
                case "Chest Armor":
                    result = this.ChestArmor != newThreshold;
                    this.ChestArmor = newThreshold;
                    break;
                case "Leg Armor":
                    result = this.LegArmor != newThreshold;
                    this.LegArmor = newThreshold;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(itemType), $"Unknown type {itemType}");
            }
            return result;
        }
    }
}
