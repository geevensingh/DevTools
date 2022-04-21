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
                            Name       = "Basic",
                            Threshold  = 136,    // effectively 68+ total
                            Mobility   = 3,
                            Resilience = 1,
                            Recovery   = 2,
                            Discipline = 2,
                            Intellect  = 2,
                            Strength   = 2,
                        },
                        new WeightSet()
                        {
                            Name = "Mobility-focused",
                            Threshold = 369,
                            Mobility = 10,
                            Resilience = 4,
                            Recovery = 5,
                            Discipline = 4,
                            Intellect = 6,
                            Strength = 4,
                        },
                    }
                },
                {
                    "Titan",
                    new HashSet<WeightSet>()
                    {
                        new WeightSet()
                        {
                            Name = "Basic",
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
                            Name = "Resilience-focused",
                            Threshold = 369,
                            Mobility = 1,
                            Resilience = 10,
                            Recovery = 5,
                            Discipline = 4,
                            Intellect = 7,
                            Strength = 4,
                        },
                    }
                },
                {
                    "Warlock",
                    new HashSet<WeightSet>()
                    {
                        new WeightSet()
                        {
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
