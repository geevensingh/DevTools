
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
        public string MasterworkTier { get; set; }
        public int MasterworkTierInt => string.IsNullOrEmpty(MasterworkTier) ? 0 : int.Parse(MasterworkTier);
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
        public int Total => AllStats.Sum();
        public bool IsSpecial =>
            AllStats.Any(x => x >= 29) ||
            AllStats.Count(x => x >= 24) > 1 ||
            AllStats.Count(x => x >= 16) > 2 ||
            AllStats.Count(x => x >= 13) > 3 ||
            AllStats.Count(x => x >= 10) > 4 ||
            AllStats.Count(x => x >= 8) > 5;

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
        public string Perks8 { get; set; }
        public string Perks9 { get; set; }
        public string Perks10 { get; set; }

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
            Perks8,
            Perks9,
            Perks10
        };

        public static HashSet<string> AllSpecialPerks { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Artifice Armor",
            "Uniformed Officer",
            "Iron Lord's Pride",
            "Visage of the Reaper",
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
