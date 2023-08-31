// See https://aka.ms/new-console-template for more information

using CsvHelper;
using CsvHelper.Configuration;
using System.Diagnostics;
using System.Globalization;
using WeaponEvaluator;

bool showFullList = false;
for (int ii = 0; ii < args.Length; ii++)
{
    string arg = args[ii];
    switch(arg.ToLower())
    {
        case "-all":
        case "-detail":
        case "-details":
        case "-showfulllist":
            showFullList = true;
            break;
    }
}

Console.Write("Getting wishlists... ");
string wishlistFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wishlist.txt");
WishList wishList = await WishList.CreateLists(wishlistFilePath);
Console.WriteLine("done");
Console.WriteLine($"wishlist saved to {wishlistFilePath}");

string weaponListFilePath = Directory.EnumerateFiles(Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE"), @"Downloads"), @"destinyWeapons*.csv").MaxBy(x => File.GetCreationTime(x));
if (!File.Exists(weaponListFilePath))
{
    Console.WriteLine("Cannot find a weapons file");
    return;
}
Console.WriteLine($"Using {weaponListFilePath}");

var csvReaderConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
{
    PrepareHeaderForMatch = args => args.Header.Replace(" ", string.Empty),
    HeaderValidated = null,
};


IEnumerable<DIMWeapon> allWeapons;
using (var reader = new StreamReader(weaponListFilePath))
using (var csv = new CsvReader(reader, csvReaderConfig))
{
    allWeapons = csv.GetRecords<DIMWeapon>()
        .Where(x => x.Tier != "Rare")
        .Where(x => x.Tier != "Exotic")
        .ToList();
}

//foreach (DIMWeapon weapon in allWeapons.Where(x => string.IsNullOrEmpty(x.Tag) || x.Tag == "junk" || x.Tag == "keep" || x.Tag == "infuse"))
//{
//    weapon.Tag = null;
//}

HashSet<DIMWeapon> untaggedWeapons = new HashSet<DIMWeapon>();
foreach (var weaponGroup in allWeapons.Where(x => string.IsNullOrEmpty(x.Tag)).GroupBy(x => x.Hash))
{
    if (wishList.GetPercentage(weaponGroup.First()).HasValue)
    {
        untaggedWeapons.UnionWith(weaponGroup);
    }
    else
    {
        Console.WriteLine($"No wish list entry for {weaponGroup.First().Name}");
    }
}

HashSet<DIMWeapon> questionableWeapons = new HashSet<DIMWeapon>();
foreach (var weapon in untaggedWeapons)
{
    if (weapon.Crafted)
    {
        weapon.SetNewTag("keep", "crafted");
        continue;
    }

    if (wishList.GetPercentage(weapon) == 100)
    {
        weapon.SetNewTag("keep", "wishlist roll");
        continue;
    }

    questionableWeapons.Add(weapon);
}

foreach (var group in questionableWeapons.GroupBy(x => x.Hash))
{
    DIMWeapon firstWeapon = group.First();
    Console.WriteLine(firstWeapon.Name);

    IEnumerable<DIMWeapon> dupes = firstWeapon.GetDupes(allWeapons);
    dupes = dupes.Except(group);

    int bestDupePercentage = 0;
    foreach (var dupe in dupes)
    {
        int dupePercentage = wishList.GetPercentage(dupe).Value;
        bestDupePercentage = Math.Max(bestDupePercentage, dupePercentage);
        Console.WriteLine($"     existing dupe  --  {dupePercentage,-5}");
    }

    bool junkDupes = false;
    foreach (DIMWeapon dimWeapon in group)
    {
        Debug.Assert(!dimWeapon.Crafted);

        int thisPercentage = wishList.GetPercentage(dimWeapon).Value;
        Debug.Assert(thisPercentage != 100);

        if (thisPercentage > bestDupePercentage)
        {
            dimWeapon.SetNewTag("keep", "better than all current dupes");
            junkDupes = true;
        }
        else if (bestDupePercentage - thisPercentage <= 25)
        {
            if (bestDupePercentage == 100)
            {
                if (dimWeapon.HasSpecialPerk)
                {
                    dimWeapon.SetNewTag("unknown", "existing item is great but this has a special perk");
                }
                else
                {
                    dimWeapon.SetNewTag("junk", "existing item is great");
                }
            }
            else
            {
                dimWeapon.SetNewTag("unknown", "about the same as a dupe");
            }
        }
        else
        {
            dimWeapon.SetNewTag("junk", "existing weapon much is better");
        }

        Console.WriteLine($"     new            --  {thisPercentage,-5}  --  id:{dimWeapon.Id}  --  {dimWeapon.GetNewTag(),-7}  --  {dimWeapon.NewTagReason}");
    }

    if (junkDupes)
    {
        var dupesToJunk = dupes.Where(x => !x.Crafted);
        if (dupesToJunk.Count() > 0)
        {
            Console.WriteLine($"junking all the dupes");
            foreach (var dupe in dupesToJunk)
            {
                dupe.SetNewTag("junk", "worse than new weapon");
            }
        }
    }
}

foreach (DIMWeapon dimWeapon in allWeapons)
{
    if (dimWeapon.GetExactDupes(allWeapons).OrderByDescending(x => x.Power).First() != dimWeapon)
    {
        dimWeapon.SetNewTag("junk", "lower level exact dupe");
    }
}

Console.WriteLine();
foreach (var newTagGroup in allWeapons.Where(x => x.TagChanged).GroupBy(x => x.GetNewTag()))
{
    foreach (var reason in newTagGroup.GroupBy(x => x.NewTagReason))
    {
        Console.WriteLine(newTagGroup.Key.PadRight(10) + " / " + reason.Key);
        Console.WriteLine("  " + string.Join("\r\n  ", reason.Select(x => $"{x.Name,-25}    id:{x.Id}")));
        Console.WriteLine(string.Join(" or ", reason.Select(x => $"id:{x.Id}")));
        Console.WriteLine();
    }
}

if (showFullList)
{
    string lastType = null;
    string lastSlot = null;
    string lastFrameStyle = null;
    var weaponGroups = WeaponGroup.CreateGroups(allWeapons);
    Dictionary<DIMWeapon, string> descriptionLookup = new Dictionary<DIMWeapon, string>();
    int maxDescriptionLength = 0;
    int maxNameLength = 0;
    foreach (var weaponGroup in weaponGroups)
    {
        if (weaponGroup.Count < 2) continue;
        foreach (var dimWeapon in weaponGroup.Weapons)
        {
            string description = (wishList.GetPercentage(dimWeapon)?.ToString() ?? "???").PadRight(3);
            if (dimWeapon.Crafted)
            {
                description += $" - crafted:{dimWeapon.CraftedLevel}";
            }

            if (string.IsNullOrEmpty(dimWeapon.GetNewTag()))
            {
                description += $" - no-tag";
            }
            else
            {
                description += $" - {dimWeapon.GetNewTag()}";
            }

            if (dimWeapon.GetSpecialPerks().Count() > 0)
            {
                description += $" - {string.Join("|", dimWeapon.GetSpecialPerks())}";
            }

            descriptionLookup[dimWeapon] = description;
            maxDescriptionLength = Math.Max(maxDescriptionLength, description.Length);
            maxNameLength = Math.Max(maxNameLength, dimWeapon.Name.Length);
        }
    }

    foreach (var weaponGroup in weaponGroups)
    {
        const string indent = "    ";
        string prefix = string.Empty;
        if (lastType != weaponGroup.Type)
        {
            Console.WriteLine($"{prefix}{weaponGroup.Type} ({weaponGroups.Where(x => x.Type == weaponGroup.Type).Sum(x => x.Count)})");
            lastType = weaponGroup.Type;
            lastSlot = null;
            lastFrameStyle = null;
        }

        prefix += indent;
        if (lastSlot != weaponGroup.Slot)
        {
            Console.WriteLine($"{prefix}{weaponGroup.Slot} ({weaponGroups.Where(x => x.Type == weaponGroup.Type && x.Slot == weaponGroup.Slot).Sum(x => x.Count)})");
            lastSlot = weaponGroup.Slot;
            lastFrameStyle = null;
        }

        prefix += indent;
        if (lastFrameStyle != weaponGroup.FrameStyle)
        {
            Console.WriteLine($"{prefix}{weaponGroup.FrameStyle.PadRight(WeaponGroup.MaxFrameStyleLength)} ({weaponGroups.Where(x => x.Type == weaponGroup.Type && x.Slot == weaponGroup.Slot && x.FrameStyle == weaponGroup.FrameStyle).Sum(x => x.Count)})");
            lastFrameStyle = weaponGroup.FrameStyle;
        }

        prefix += indent;
        Console.WriteLine($"{prefix}{weaponGroup.Element.PadRight(8)} -- {weaponGroup.Count}{indent}{weaponGroup.DIMQuery}");

        prefix += indent;
        if (weaponGroup.Count > 1)
        {
            ConsoleColor consoleColor = ConsoleColor.Green;
            if (weaponGroup.Count >= 3)
            {
                consoleColor = ConsoleColor.Yellow;
            }
            if (weaponGroup.Count >= 6)
            {
                consoleColor = ConsoleColor.Red;
            }

            ConsoleColor originalColor = Console.ForegroundColor;
            Console.ForegroundColor = consoleColor;
            foreach (var dimWeapon in weaponGroup.Weapons)
            {
                string description = descriptionLookup[dimWeapon];
                description = $"({description})".PadRight(maxDescriptionLength + 3);
                Console.WriteLine($"{prefix}{dimWeapon.Name.PadRight(maxNameLength + 1)} {description} id:{dimWeapon.Id}");
            }
            Console.ForegroundColor = originalColor;
        }
    }
}
