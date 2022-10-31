// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using WeaponEvaluator;

internal class DIMWeapon
{
    private string newTag = null;

    public string Name { get; set; }
    public string Hash { get; set; }
    public string Id { get; set; }
    public string Tag { get; set; }
    public string Tier { get; set; }
    public string Type { get; set; }
    public string Source { get; set; }
    public string Category { get; set; }
    public string Element { get; set; }
    public string Power { get; set; }
    public string PowerLimit { get; set; }
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
    public string Equip { get; set; }
    public string ChargeTime { get; set; }
    public string DrawTime { get; set; }
    public string Accuracy { get; set; }
    public string ChargeRate { get; set; }
    public string GuardResistance { get; set; }
    public string GuardEfficiency { get; set; }
    public string GuardEndurance { get; set; }
    public string SwingSpeed { get; set; }
    public string ShieldDuration { get; set; }
    public string AirborneEffectiveness { get; set; }
    public bool Crafted { get; set; }
    public string CraftedLevel { get; set; }
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
    public string Perks13 { get; set; }
    public string Perks14 { get; set; }
    public string Perks15 { get; set; }

    public string[] Perks
    {
        get
        {
            string[] perks = new string[] { Perks0, Perks1, Perks2, Perks3, Perks4, Perks5, Perks6, Perks7, Perks8, Perks9, Perks10, Perks11, Perks12, Perks13, Perks14, Perks15 };
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
        switch (this.Type)
        {
            case "Auto Rifle":
                return new string[]
                {
                    "Subsistence",
                };
            case "Fusion Rifle":
                return new string[]
                {
                    "Reservoir Burst",
                };
            case "Grenade Launcher":
                if (this.Category == "Power")
                {
                    return new string[]
                    {
                        "Ambitious Assassin",
                    };
                }
                return new string[]
                {
                    "Ambitious Assassin",
                };
            case "Linear Fusion Rifle":
                if (this.Category == "Power")
                {
                    return new string[]
                    {
                        "Clown Cartridge",
                        "Auto-Loading Holster",
                    };
                }
                return new string[]
                {
                    "Auto-Loading Holster",
                };
            case "Machine Gun":
                return new string[]
                {
                    "Subsistence",
                    "Auto-Loading Holster",
                    "Thresh",
                };
            case "Rocket Launcher":
                return new string[]
                {
                    "Ambitious Assassin",
                    "Auto-Loading Holster",
                };
            case "Sidearm":
                return new string[]
                {
                    "Surrounded",
                };
            case "Submachine Gun":
                return new string[]
                {
                    "Surrounded",
                };
            case "Sword":
                return new string[]
                {
                    "Surrounded",
                };

            case "Combat Bow":
            case "Glaive":
            case "Hand Cannon":
            case "Pulse Rifle":
            case "Scout Rifle":
            case "Shotgun":
            case "Sniper Rifle":
            case "Trace Rifle":
                return new string[] { };
            default:
                throw new ArgumentException($"Unknown weapon type: {this.Type}");
        }
    }

    public bool HasSpecialPerk => this.GetSpecialPerks().Count() > 0;
}