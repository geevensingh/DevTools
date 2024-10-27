// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using WeaponEvaluator;

internal class DIMWeapon
{
    private string? newTag = null;

    public string Name { get; set; }
    public string Hash { get; set; }
    public string Id { get; set; }
    public string Tag { get; set; }
    public string Tier { get; set; }
    public string Type { get; set; }
    public string Source { get; set; }
    public string Category { get; set; }
    public string Element { get; set; }
    public int Power { get; set; }
    public string MasterworkType { get; set; }
    public string MasterworkTier { get; set; }
    public string Owner { get; set; }
    public string Locked { get; set; }
    public string Equipped { get; set; }
    public string Year { get; set; }
    public string Season { get; set; }
    public string Event { get; set; }
    public string Recoil { get; set; }
    public string AA { get; set; }
    public string Impact { get; set; }
    public string Range { get; set; }
    public string Zoom { get; set; }
    public string BlastRadius { get; set; }
    public string Velocity { get; set; }
    public string Stability { get; set; }
    public string ROF { get; set; }
    public string Reload { get; set; }
    public string Mag { get; set; }
    public string Handling { get; set; }
    public string ChargeTime { get; set; }
    public string DrawTime { get; set; }
    public string Accuracy { get; set; }
    public string ChargeRate { get; set; }
    public string GuardResistance { get; set; }
    public string GuardEndurance { get; set; }
    public string SwingSpeed { get; set; }
    public string ShieldDuration { get; set; }
    public string AirborneEffectiveness { get; set; }
    public string Crafted { get; set; }
    public bool IsCrafted
    {
        get
        {
            if (bool.TryParse(this.Crafted, out bool result))
            {
                return result;
            }
            return false;
        }
    }
    public int CraftedLevel { get; set; }
    public string KillTracker { get; set; }
    public string Foundry { get; set; }
    public string Notes { get; set; }
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
    public string Perks11 { get; set; }
    public string Perks12 { get; set; }

    public string[] Perks
    {
        get
        {
            string[] perks = new string[] { Perks0, Perks1, Perks2, Perks3, Perks4, Perks5, Perks6, Perks7, Perks8, Perks9, Perks10, Perks11, Perks12 };
            perks = perks.Select(x => x.Trim().TrimEnd('*').Trim()).Where(x => !string.IsNullOrEmpty(x)).ToArray();
            perks = perks.Where(x => x != "Kill Tracker" && x != "Crucible Tracker" && x != "Shaped Weapon" && x != "Empty Memento Socket").ToArray();
            return perks;

        }
    }

    public bool HasPerk(string perk) => this.Perks.Contains(perk);
    public bool HasAllPerks(IEnumerable<string> perks) => perks.All(x => this.HasPerk(x));

    public long HashAsLong => long.Parse(this.Hash);

    public bool IsDupe(IEnumerable<DIMWeapon> allWeapons)
    {
        return this.GetDupes(allWeapons).Count() > 1;
    }

    public IEnumerable<DIMWeapon> GetDupes(IEnumerable<DIMWeapon> allWeapons)
    {
        return allWeapons.Where(x => x.Hash == this.Hash);
    }

    public bool TagChanged => this.GetNewTag() != this.Tag;
    public string NewTagReason { get; private set; }
    public string GetNewTag() => newTag ?? this.Tag;
    public void SetNewTag(string newTag, string reason)
    {
        this.newTag = newTag;
        this.NewTagReason = reason;
    }

    public IEnumerable<DIMWeapon> GetExactDupes(IEnumerable<DIMWeapon> allWeapons)
    {
        var dupes = this.GetDupes(allWeapons);
        if (!dupes.Any())
        {
            return dupes;
        }

        string localPerks = string.Join("//", this.Perks);
        dupes = dupes.Where(x => string.Join("//", x.Perks) == localPerks);
        return dupes;
    }

    public IEnumerable<string> GetSpecialPerks()
    {
        return GetPotentialSpecialPerks().Where(x => this.HasPerk(x));
    }

    private IEnumerable<string> GetPotentialSpecialPerks()
    {
        List<string> results = new List<string>()
        {
            // Arc
            "Voltshot",
            // Solar
            "Incandescent",
            // Void
            "Repulsor Brace",
            "Destabilizing Rounds",
            // Stasis
            "Headstone",
            // Kinetic
            "Kinetic Tremors",
            "Osmosis",
            // Strand
            "Hatchling",
            "Slice",

            // General
            "Reconstruction",
            "Subsistence",
            "Chain Reaction",
            "Vorpal Weapon",
            "Rewind Rounds",
            "Permeability",
        };
        switch (this.Type)
        {
            case "Auto Rifle":
                results.Add("Frenzy");
                break;
            case "Fusion Rifle":
                results.Add("Auto-Loading Holster");
                results.Add("Reservoir Burst");
                break;
            case "Grenade Launcher":
                results.Add("Auto-Loading Holster");
                results.Add("Ambitious Assassin");
                results.Add("Disorienting Grenades");
                break;
            case "Linear Fusion Rifle":
                results.Add("Auto-Loading Holster");
                if (this.Category == "Power")
                {
                    results.Add("Clown Cartridge");
                    results.Add("Firing Line");
                }
                break;
            case "Machine Gun":
                results.Add("Auto-Loading Holster");
                results.Add("Thresh");
                results.Add("Frenzy");
                break;
            case "Rocket Launcher":
                results.Add("Ambitious Assassin");
                results.Add("Auto-Loading Holster");
                break;
            case "Sidearm":
                results.Add("Frenzy");
                results.Add("Perpetual Motion");
                results.Add("Threat Detector");
                results.Add("Surrounded");
                results.Add("Thresh");
                break;
            case "Submachine Gun":
                results.Add("Frenzy");
                results.Add("Perpetual Motion");
                results.Add("Threat Detector");
                results.Add("Surrounded");
                results.Add("Thresh");
                break;
            case "Sword":
                results.Add("Eager Edge");
                results.Add("Perpetual Motion");
                results.Add("Threat Detector");
                results.Add("Surrounded");
                results.Add("Whirlwind Blade");
                results.Add("Tireless Blade");
                break;
            case "Shotgun":
                results.Add("Auto-Loading Holster");
                results.Add("Threat Detector");
                results.Add("Surrounded");
                results.Add("Trench Barrel");
                break;
            case "Sniper Rifle":
                results.Add("Auto-Loading Holster");
                results.Add("Clown Cartridge");
                results.Add("Firing Line");
                results.Add("No Distractions");
                results.Add("Snapshot Sights");
                results.Add("Keep Away");
                break;
            case "Scout Rifle":
                results.Add("Keep Away");
                break;
            case "Combat Bow":
            case "Glaive":
            case "Hand Cannon":
            case "Pulse Rifle":
            case "Trace Rifle":
                break;
            default:
                throw new ArgumentException($"Unknown weapon type: {this.Type}");
        }

        return results;
    }

    public bool HasSpecialPerk => this.GetSpecialPerks().Count() > 0;

    public string FrameStyle
    {
        get
        {
            if (this.Tier == "Exotic")
            {
                return "Exotic";
            }

            return this.Perks.First();
        }
    }
}