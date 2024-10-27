
namespace ArmorEvaluator
{
    using CsvHelper.Configuration.Attributes;
    using System.Diagnostics.CodeAnalysis;

    public record Item
    {
        public string Name { get; set; }
        public string Hash { get; set; }
        public string Id { get; set; }
        public string Tag { get; set; }
        public string Tier { get; set; }
        public string Type { get; set; }
        public string Equippable { get; set; }
        public int Power { get; set; }
        public string EnergyCapacity { get; set; }
        public int EnergyCapacityInt => string.IsNullOrEmpty(EnergyCapacity) ? 0 : int.Parse(EnergyCapacity);
        [Name("Mobility (Base)")]
        public int Mobility { get; set; }
        [Name("Resilience (Base)")]
        public int Resilience { get; set; }
        [Name("Recovery (Base)")]
        public int Recovery { get; set; }
        [Name("Discipline (Base)")]
        public int Discipline { get; set; }
        [Name("Intellect (Base)")]
        public int Intellect { get; set; }
        [Name("Strength (Base)")]
        public int Strength { get; set; }
        public string SeasonalMod { get; set; }

        public int[] AllStats => new int[] { Mobility, Resilience, Recovery, Discipline, Intellect, Strength };
        public int[] AllStatsAdjusted
        {
            get
            {
                if ((Equippable == "Warlock") || (Equippable == "Titan"))
                {
                     return new int[] { Resilience, Recovery, Discipline, Intellect, Strength };
                }
                return AllStats;
            }
        }
        public int Total => AllStats.Sum();
        public int SpecialLevel
        {
            get
            {
                int[] allStats = this.AllStatsAdjusted;
                if (this.EnergyCapacityInt == 10)
                {
                    allStats = allStats.Select(x => x + 1).ToArray();
                }
                
                if (allStats.Count(x => x >= 30) >= 1)
                {
                    return 1;
                }
                if (allStats.Count(x => x >= 24) >= 1 && allStats.Count(x => x >= 18) >= 2)
                {
                    return 2;
                }
                if (allStats.Count(x => x >= 18) >= 2 && allStats.Count(x => x >= 12) >= 3)
                {
                    return 3;
                }
                if (allStats.Count(x => x >= 13) >= 3 && allStats.Count(x => x >= 9) >= 4)
                {
                    return 4;
                }
                if (allStats.Count(x => x >= 11) >= 4 && allStats.Count(x => x >= 5) >= 5)
                {
                    return 5;
                }
                if (allStats.Count(x => x >= 9) >= 5 && allStats.Count(x => x >= 5) >= 6)
                {
                    return 6;
                }

                return -1;
            }
        }

        public bool IsClassItem =>
            (Type == "Hunter Cloak") ||
            (Type == "Titan Mark") ||
            (Type == "Warlock Bond");

        public string Perks0 { get; set; }
        public string Perks1 { get; set; }
        public string Perks2 { get; set; }
        public string Perks3 { get; set; }
        public string Perks4 { get; set; }
        public string Perks5 { get; set; }
        public string Perks6 { get; set; }
        public string Perks7 { get; set; }

        public List<string> Perks => new List<string>()
        {
            Perks0,
            Perks1,
            Perks2,
            Perks3,
            Perks4,
            Perks5,
            Perks6,
            Perks7,
        };

        public static HashSet<string> AllSpecialPerks { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Artifice Armor",
            "Uniformed Officer",
            "Iron Lord's Pride",
            "No Kindling Added",
            "Small Kindling",
            "Large Kindling",
            "Fully Rekindled",
            "Plunderer's Trappings",
            "Queen's Favor",
            "Sonar Amplifier",
            "Exhumed Excess",
            "Ascendant Protector",
            "Echoes of Glory",
            "Eido's Apprentice",
        };

        public HashSet<string> SpecialPerks => Perks.Where(x => AllSpecialPerks.Contains(x.Trim('*'))).ToHashSet(StringComparer.OrdinalIgnoreCase);

        public string UniqueType
        {
            get
            {
                return $"{string.Join("-", SpecialPerks)}-{SeasonalMod}";
            }
        }

        //public bool Equals(Item? other)
        //{
        //    if (ReferenceEquals(null, other)) return false;
        //    if (ReferenceEquals(this, other)) return true;
        //    return this.Id == other.Id;
        //}

        //public override bool Equals(object? obj)
        //{
        //    return this.Equals(obj as Item);
        //}

        //public override int GetHashCode()
        //{
        //    return int.Parse(this.Id);
        //}
    }
}
