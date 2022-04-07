using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArmorEvaluator
{
    public class WeightSet
    {
        public string Name { get; set; }
        public string Class { get; set; }
        public float Threshold { get; set; }

        public float Mobility { get; set; }
        public float Resilience { get; set; }
        public float Recovery { get; set; }
        public float Discipline { get; set; }
        public float Intellect { get; set; }
        public float Strength { get; set; }

        public static Dictionary<string, HashSet<WeightSet>> GetDefaults()
        {
            return new Dictionary<string, HashSet<WeightSet>>()
            {
                {
                    "Hunter",
                    new HashSet<WeightSet>()
                    {
                        new WeightSet()
                        {
                            Class = "Hunter",
                            Name = "Basic",
                            Threshold = 340,
                            Mobility = 10,
                            Resilience = 4,
                            Recovery = 5,
                            Discipline = 4,
                            Intellect = 7,
                            Strength = 4,
                        }
                    }
                },
                {
                    "Titan",
                    new HashSet<WeightSet>()
                    {
                        new WeightSet()
                        {
                            Class = "Titan",
                            Name = "Basic",
                            Threshold = 310,
                            Mobility = 1,
                            Resilience = 10,
                            Recovery = 5,
                            Discipline = 4,
                            Intellect = 7,
                            Strength = 4,
                        }
                    }
                },
                {
                    "Warlock",
                    new HashSet<WeightSet>()
                    {
                        new WeightSet()
                        {
                            Class = "Warlock",
                            Name       = "Basic",
                            Threshold  = 136,    // effectively 68+ total
                            Mobility   = 1,
                            Resilience = 2,
                            Recovery   = 2,
                            Discipline = 2,
                            Intellect  = 3,
                            Strength   = 2,
                        },
                        new WeightSet()
                        {
                            Class = "Warlock",
                            Name       = "Grenade",
                            Threshold  = 268,
                            Mobility   = 0.1f,
                            Resilience = 2,
                            Recovery   = 6,
                            Discipline = 7,
                            Intellect  = 5,
                            Strength   = 4,
                        },
                        new WeightSet()
                        {
                            Class = "Warlock",
                            Name       = "Super",
                            Threshold  = 268,
                            Mobility   = 0.1f,
                            Resilience = 2,
                            Recovery   = 6,
                            Discipline = 5,
                            Intellect  = 7,
                            Strength   = 4,
                        },
                    }
                },
            };
        }
    }
}
