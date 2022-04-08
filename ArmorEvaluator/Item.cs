
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
        public int MasterworkTier { get; set; }
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
            AllStats.Any(x => x >= 23) ||
            AllStats.Count(x => x >= 19) > 1 ||
            AllStats.Count(x => x >= 17) > 2;

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
