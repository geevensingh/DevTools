// See https://aka.ms/new-console-template for more information

using CsvHelper;
using CsvHelper.Configuration;
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
        .ToList();
}

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

foreach (var group in untaggedWeapons.GroupBy(x => x.Hash))
{
    DIMWeapon firstWeapon = group.First();
    Console.WriteLine(firstWeapon.Name);

    IEnumerable<DIMWeapon> dupes = firstWeapon.GetDupes(allWeapons);
    dupes = dupes.Except(group);

    int bestPercentage = 0;
    foreach (var dupe in dupes)
    {
        int dupePercentage = wishList.GetPercentage(dupe).Value;
        bestPercentage = Math.Max(bestPercentage, dupePercentage);
        Console.WriteLine($"     existing dupe  --  {dupePercentage,-5}");
    }

    bool junkDupes = false;
    foreach (DIMWeapon dimWeapon in group)
    {
        if (dimWeapon.Crafted)
        {
            dimWeapon.SetNewTag("keep", "crafted");
            continue;
        }

        int thisPercentage = wishList.GetPercentage(dimWeapon).Value;
        if (thisPercentage == 100)
        {
            dimWeapon.SetNewTag("keep", "strongly recommended");
        }
        else if (thisPercentage > bestPercentage)
        {
            dimWeapon.SetNewTag("keep", "better than all current dupes");
            junkDupes = true;
        }
        else if (bestPercentage - thisPercentage <= 25)
        {
            if (bestPercentage == 100 && !dimWeapon.HasSpecialPerk)
            {
                dimWeapon.SetNewTag("junk", "existing item is great");
            }
            else
            {
                dimWeapon.SetNewTag("unknown", "about the same as a dupe");
            }
        }
        else
        {
            dimWeapon.SetNewTag("junk", "existing weapon is better");
        }

        Console.WriteLine($"     new            --  {thisPercentage,-5}  --  id:{dimWeapon.Id}  --  {dimWeapon.Tag}  --  {dimWeapon.NewTagReason}");
    }

    if (junkDupes && dupes.Count() > 0)
    {
        Console.WriteLine($"junking all the dupes");
        foreach (var dupe in dupes)
        {
            dupe.SetNewTag("junk", "worse than new weapon");
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
foreach (var reason in allWeapons.Where(x => x.TagChanged).GroupBy(x => x.NewTagReason))
{
    Console.WriteLine(reason.First().GetNewTag().PadRight(10) + " / " + reason.Key);
    Console.WriteLine("  " + string.Join("\r\n  ", reason.Select(x => $"{x.Name,-25}    id:{x.Id}")));
    Console.WriteLine(string.Join(" or ", reason.Select(x => $"id:{x.Id}")));
    Console.WriteLine();
}

if (showFullList)
{
    string lastType = null;
    string lastSlot = null;
    string lastElement = null;
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
            lastElement = null;
        }

        prefix += indent;
        if (lastSlot != weaponGroup.Slot)
        {
            Console.WriteLine($"{prefix}{weaponGroup.Slot} ({weaponGroups.Where(x => x.Type == weaponGroup.Type && x.Slot == weaponGroup.Slot).Sum(x => x.Count)})");
            lastSlot = weaponGroup.Slot;
            lastElement = null;
        }

        prefix += indent;
        if (lastElement != weaponGroup.Element)
        {
            Console.WriteLine($"{prefix}{weaponGroup.Element} ({weaponGroups.Where(x => x.Type == weaponGroup.Type && x.Slot == weaponGroup.Slot && x.Element == weaponGroup.Element).Sum(x => x.Count)})");
            lastElement = weaponGroup.Element;
        }

        prefix += indent;
        Console.WriteLine($"{prefix}{weaponGroup.FrameStyle.PadRight(WeaponGroup.MaxFrameStyleLength)} -- {weaponGroup.Count}{indent}{weaponGroup.DIMQuery}");

        prefix += indent;
        if (weaponGroup.Count > 1)
        {
            foreach (var dimWeapon in weaponGroup.Weapons)
            {
                string description = descriptionLookup[dimWeapon];
                description = $"({description})".PadRight(maxDescriptionLength + 3);
                Console.WriteLine($"{prefix}{dimWeapon.Name.PadRight(maxNameLength + 1)} {description} id:{dimWeapon.Id}");
            }
        }
    }
    //foreach (var type in allWeapons.Where(x => x.Tier != "Exotic").Where(x => x.GetNewTag() != "junk" && x.GetNewTag() != "influse" && (!x.Crafted || x.CraftedLevel >= 10)).GroupBy(x => x.Type))
    //{
    //    foreach (var typeSlot in type.GroupBy(x => x.Category))
    //    {
    //        Console.WriteLine($"{type.Key} - {typeSlot.Key}  ({type.Count()})");
    //        foreach (var typeSlotElement in typeSlot.GroupBy(x => x.Element))
    //        {
    //            if (typeSlotElement.Count() > 1)
    //            {
    //                Console.WriteLine($"     {typeSlotElement.Key}   ({typeSlotElement.Count()})");
    //                foreach (var typeSlotElementFrameStyle in typeSlotElement.GroupBy(x => x.FrameStyle))
    //                {
    //                    if (typeSlotElementFrameStyle.Count() > 1)
    //                    {
    //                        Console.WriteLine($"          {typeSlotElementFrameStyle.Key.PadLeft(20)}  ({typeSlotElementFrameStyle.Count()})  is:{type.Key.Replace(" ", null)} is:{typeSlotElement.Key} perkname:{typeSlotElementFrameStyle.Key.Foo()}");
    //                        foreach (var dimWeapon in typeSlotElementFrameStyle)
    //                        {
    //                            string description = (wishList.GetPercentage(dimWeapon)?.ToString() ?? "???").PadRight(3);
    //                            if (dimWeapon.Crafted)
    //                            {
    //                                description += $" - crafted:{dimWeapon.CraftedLevel}";
    //                            }

    //                            if (string.IsNullOrEmpty(dimWeapon.GetNewTag()))
    //                            {
    //                                description += $" - no-tag";
    //                            }
    //                            else
    //                            {
    //                                description += $" - {dimWeapon.GetNewTag()}";
    //                            }

    //                            if (dimWeapon.GetSpecialPerks().Count() > 0)
    //                            {
    //                                description += $" - {string.Join("|", dimWeapon.GetSpecialPerks())}";
    //                            }

    //                            description = $"({description})".PadRight(50);
    //                            Console.WriteLine($"               {dimWeapon.Name.PadRight(32)}   {description}     id:{dimWeapon.Id}");
    //                        }
    //                    }
    //                }
    //            }
    //        }
    //    }
    //}
}
