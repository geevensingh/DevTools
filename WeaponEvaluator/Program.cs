// See https://aka.ms/new-console-template for more information

using CsvHelper;
using CsvHelper.Configuration;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using Utilities;
using WeaponEvaluator;

Console.Write("Getting wishlists... ");
var lists = await WeaponList.CreateLists();
WeaponList wishList = lists.Item1;
WeaponList trashList = lists.Item2;
Console.WriteLine("done");

string weaponListFilePath = Directory.EnumerateFiles(Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE"), @"Downloads"), @"destinyWeapons*.csv").MaxBy(x => File.GetCreationTime(x));
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

IEnumerable<DIMWeapon> untaggedWeapons = allWeapons.Where(x => string.IsNullOrEmpty(x.Tag));
foreach (DIMWeapon dimWeapon in untaggedWeapons)
{
    if (dimWeapon.Crafted)
    {
        dimWeapon.SetNewTag("keep", "crafted");
    }
    else
    {
        int thisPercentage = wishList.GetPercentage(dimWeapon);
        if (thisPercentage == 100)
        {
            dimWeapon.SetNewTag("keep", "strongly recommended");
        }
        else
        {
            IEnumerable<DIMWeapon> dupes = dimWeapon.GetDupes(allWeapons);
            if (dimWeapon.HasSpecialPerk)
            {
                IEnumerable<string> specialPerks = dimWeapon.GetSpecialPerks();
                dupes = dupes.Where(x => x.HasAllPerks(specialPerks));
            }
            dupes = dupes.Except(new DIMWeapon[] { dimWeapon });

            int bestPercentage = 0;
            Console.WriteLine($"{dimWeapon.Name,-30}  --  {thisPercentage,-5} -- id:{dimWeapon.Id}");
            foreach (var dupe in dupes)
            {
                int dupePercentage = wishList.GetPercentage(dupe);
                bestPercentage = Math.Max(bestPercentage, dupePercentage);
                Console.WriteLine($"{"another dupe",30}  --  {dupePercentage,-5}");
            }

            if (thisPercentage > bestPercentage)
            {
                dimWeapon.SetNewTag("keep", "better than all current dupes");
                foreach (var dupe in dupes)
                {
                    dimWeapon.SetNewTag("junk", "worse than new weapon");
                }
            }
            else if (bestPercentage - thisPercentage <= 25)
            {
                dimWeapon.SetNewTag("unknown", "about the same as a dupe");
            }
            else
            {
                dimWeapon.SetNewTag("junk", "existing weapon is better");
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
foreach (var reason in allWeapons.Where(x => x.TagChanged).GroupBy(x => x.NewTagReason))
{
    Console.WriteLine(reason.Key);
    Console.WriteLine("  " + string.Join("\r\n  ", reason.Select(x => $"{x.Name,-25}    id:{x.Id}")));
    Console.WriteLine(string.Join(" or ", reason.Select(x => $"id:{x.Id}")));
}
Console.WriteLine();
