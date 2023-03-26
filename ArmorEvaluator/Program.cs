// See https://aka.ms/new-console-template for more information

using ArmorEvaluator;
using CsvHelper;
using CsvHelper.Configuration;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Globalization;

string weightsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Weights.json");

Console.WriteLine($"Using weights from {weightsPath}");
var weights = JsonConvert.DeserializeObject<Dictionary<string, HashSet<WeightSet>>>(File.ReadAllText(weightsPath));

var config = new CsvConfiguration(CultureInfo.InvariantCulture)
{
    PrepareHeaderForMatch = args => args.Header.Replace(" ", string.Empty),
};

string filePath = null;
bool makeSpreadsheet = false;
foreach (string arg in args)
{
    if (File.Exists(arg))
    {
        filePath = arg;
    }
    else if (arg.ToLower() == "-sheet")
    {
        makeSpreadsheet = true;
    }

}

if (string.IsNullOrEmpty(filePath))
{
    filePath = Directory.EnumerateFiles(Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE"), @"Downloads"), @"destinyArmor*.csv").MaxBy(x => File.GetCreationTime(x));
}
Console.WriteLine($"Using {filePath}");

IEnumerable<Item> allItems;
using (var reader = new StreamReader(filePath))
using (var csv = new CsvReader(reader, config))
{
    allItems = csv.GetRecords<Item>()
        .Where(x => x.Tier != "Rare")
        //.Where(x => !x.IsClassItem)
        .ToList();
}

var comparer = new Comparer();
var appliedWeights = allItems.Select(item => new ScratchPad(item, AppliedWeightSet.Create(item, weights[item.Equippable]))).ToHashSet(comparer);


// Assume we're keeping everything
foreach (var item in appliedWeights)
{
    if (item.Item.Tag == "favorite")
    {
        item.SetTag(NewTagKind.Favorite, "already tagged as favorite");
        continue;
    }

    if (item.Item.EnergyCapacityInt == 10)
    {
        item.SetTag(NewTagKind.AbsoluteKeep, "is masterwork already");
        continue;
    }
    
    if (item.Item.SeasonalMod.Contains("artifice"))
    {
        item.SetTag(NewTagKind.AbsoluteKeep, "keep all artifice armor");
        continue;
    }
    
    if (item.MeetsThreshold)
    {
        item.SetTag(NewTagKind.Keep, $"meets threshold ({item.WeightsThatMeetThreshold})");
        continue;
    }
    
    if (item.SpecialLevel > 0)
    {
        item.SetTag(NewTagKind.Keep, $"is special {item.SpecialLevel}");
        continue;
    }

    item.SetTag(NewTagKind.Junk, "does not meet threshold or masterwork");
}

var initiallyJunk = appliedWeights.Where(x => x.IsJunk).ToHashSet(comparer);

// Look for items that aren't junk, but are worse than all dupes
foreach (var dupes in appliedWeights.Where(x => !x.Item.IsClassItem).GroupBy(x => x.Item.Hash))
{
    var bestAtSomething = new HashSet<ScratchPad>(comparer);
    foreach (var applicableWeightSet in dupes.First().Weights.Select(x => x.WeightSet))
    {
        bestAtSomething.Add(dupes.MaxBy(x => x.Weights.Single(y => y.WeightSet == applicableWeightSet).Sum));
    }
    foreach (var dupe in dupes.Except(bestAtSomething).Where(x => x.CanChangeTag && x.SpecialLevel <= 0))
    {
        dupe.SetTag(NewTagKind.Junk, "is worse than a dup for every weight set");
    }
}

foreach (var hash in initiallyJunk.Where(x => !x.Item.IsClassItem).Select(x => x.Item.Hash).Distinct())
{
    var dupes = appliedWeights.Where(x => x.Item.Hash == hash);
    if (dupes.Count() > 1)
    {
        foreach (var dupe in dupes.Where(x => x.CanChangeTag))
        {
            var otherDupes = dupes.Where(x => x.Item.Id != dupe.Item.Id);
            if (dupe.Item.Mobility <= otherDupes.Select(x => x.Item.Mobility).Max() &&
                dupe.Item.Resilience <= otherDupes.Select(x => x.Item.Resilience).Max() &&
                dupe.Item.Recovery <= otherDupes.Select(x => x.Item.Recovery).Max() &&
                dupe.Item.Discipline <= otherDupes.Select(x => x.Item.Discipline).Max() &&
                dupe.Item.Intellect <= otherDupes.Select(x => x.Item.Intellect).Max() &&
                dupe.Item.Strength <= otherDupes.Select(x => x.Item.Strength).Max() &&
                dupe.Item.Total < otherDupes.Select(x => x.Item.Total).Max())
            {
                dupe.SetTag(NewTagKind.Junk, $"worse than all other {dupe.Item.Name}");
            }
            else if (dupe.Item.SpecialLevel < 0)
            {
                foreach (var otherDupe in otherDupes)
                {
                    if (AppliedWeightSet.IsLatterMuchBetter(dupe.Weights, otherDupe.Weights, dupe.Item.Tier == "Exotic"))
                    {
                        dupe.SetTag(NewTagKind.Junk, $"for every weight set, is worse than another of the same item");
                        break;
                    }
                }
            }
        }
    }
}


// Look for items that are strictly worse that others
var bestDupes = new HashSet<ScratchPad>();
foreach (var eval in initiallyJunk)
{
    if (bestDupes.Contains(eval))
    {
        continue;
    }

    var dupeSet = appliedWeights
        .Where(x => x.Item.Tier != "Exotic" || x.Item.Hash == eval.Item.Hash)
        .Where(x => x.Item.Tier == eval.Item.Tier)
        .Where(x => x.Item.Type == eval.Item.Type)
        .Where(x => x.Item.Equippable == eval.Item.Equippable)
        .Where(x => x.Item.UniqueType == eval.Item.UniqueType)
        .Where(x => x.Item.Total >= eval.Item.Total)
        .Where(x => x.Item.Mobility >= eval.Item.Mobility)
        .Where(x => x.Item.Resilience >= eval.Item.Resilience)
        .Where(x => x.Item.Recovery >= eval.Item.Recovery)
        .Where(x => x.Item.Discipline >= eval.Item.Discipline)
        .Where(x => x.Item.Intellect >= eval.Item.Intellect)
        .Where(x => x.Item.Strength >= eval.Item.Strength);
    if (dupeSet.Count() > 1)
    {
        var bestDupe = GetSingleBest(dupeSet);
        bestDupes.Add(bestDupe);
        if (bestDupe != eval)
        {
            eval.SetTag(NewTagKind.Junk, $"strictly worse than {bestDupe.Item.Name} ({bestDupe.Item.Id})");
        }
    }
}

// delete only the best "special" for each slot for each attribute
var specialKeepers = appliedWeights.Where(x => x.NewTagReason.Contains("Keep -> is special"));
var bestKeepers = new HashSet<ScratchPad>();
foreach (var items in specialKeepers.GroupBy(x => x.NewTagReason + " - " + x.Item.Equippable + " - " + x.Item.Type))
{
    int bestTotal = items.Max(x => x.Item.AllStatsAdjusted.Sum());
    bestKeepers.UnionWith(items.Where(x => x.Item.AllStatsAdjusted.Sum() >= bestTotal * 0.96));

    int statCount = items.First().Item.AllStatsAdjusted.Length;
    for (int ii = 0; ii < statCount; ii++)
    {
        int bestValue = items.Max(x => x.Item.AllStatsAdjusted[ii]);
        var bestGroup = items.Where(x => x.Item.AllStatsAdjusted[ii] == bestValue);
        bestTotal = bestGroup.Max(x => x.Item.AllStatsAdjusted.Sum());
        bestKeepers.UnionWith(bestGroup.Where(x => x.Item.AllStatsAdjusted.Sum() == bestTotal));
    }
}
foreach (var item in specialKeepers.Except(bestKeepers))
{
    item.SetTag(NewTagKind.Junk, $"is special {item.SpecialLevel}, but not the best special");
}

// Make sure we don't delete everything with a specific seasonal mod slot
var allJunk = appliedWeights.Where(x => x.IsJunk);
foreach (var c in appliedWeights.GroupBy(x => x.Item.Equippable))
{
    foreach (var type in c.GroupBy(x => x.Item.Type))
    {
        foreach (var modType in type.GroupBy(x => x.Item.UniqueType))
        {
            if (modType.All(x => allJunk.Contains(x)))
            {
                GetSingleBest(modType).SetTag(NewTagKind.Keep, $"best {c.Key} {type.Key} {modType.Key}");
            }
        }
    }
}

// Make sure we don't delete all of a given exotic
allJunk = appliedWeights.Where(x => x.IsJunk);
foreach (var itemHash in allJunk.Where(x => x.Item.Tier == "Exotic").Select(x => x.Item.Hash).Distinct())
{
    var items = appliedWeights.Where(x => x.Item.Hash == itemHash);
    if (items.All(x => allJunk.Contains(x)))
    {
        GetSingleBest(items).SetTag(NewTagKind.Keep, $"best exotic {items.First().Item.Name}");
    }
}

// Make sure we don't delete the highest power in any slot
allJunk = appliedWeights.Where(x => x.IsJunk);
foreach (var c in appliedWeights.GroupBy(x => x.Item.Equippable))
{
    foreach (var type in c.GroupBy(x => x.Item.Type))
    {
        int highestPower = type.Max(x => x.Item.Power);
        var inSlotJunk = allJunk
            .Where(x => x.Item.Equippable == c.Key)
            .Where(x => x.Item.Type == type.Key);
        int junkCountAtHighestPower = inSlotJunk
            .Count(x => x.Item.Power == highestPower);
        if (type.Count(x => x.Item.Power == highestPower) == junkCountAtHighestPower)
        {
            var toInfuse = inSlotJunk.Where(x => x.Item.Power == highestPower);
            foreach (var item in toInfuse) { item.SetTag(NewTagKind.Infuse, $"highest power in {c.Key} {type.Key}"); }
        }
    }
}

// Mark for infusion all junk with a lower power masterwork dupe
var infusionTargets = new HashSet<ScratchPad>();
foreach (var hash in initiallyJunk.Where(x => x.IsJunk).GroupBy(x => x.Item.Hash))
{
    var bestJunk = hash.MaxBy(x => x.Item.Power);
    var masterworkDupe = appliedWeights
        .Where(x => x.Item.Hash == hash.Key)
        .Where(x => x.Item.Power < bestJunk.Item.Power)
        .Where(x => x.Item.EnergyCapacityInt == 10 || (x.Item.Tier == "Exotic" && x.Item.EnergyCapacityInt >= 7));
    if (masterworkDupe.Any())
    {
        bestJunk.SetTag(NewTagKind.Infuse, "use to improve a masterwork");
        infusionTargets.Add(GetSingleBest(masterworkDupe));
    }
}

// Mark for infusion all junk with a MUCH lower power dupe
foreach (var hash in initiallyJunk.Where(x => x.IsJunk).GroupBy(x => x.Item.Hash))
{
    var bestJunk = hash.MaxBy(x => x.Item.Power);
    var reallyLowDupe = appliedWeights
        .Where(x => x.Item.Hash == hash.Key)
        .Where(x => bestJunk.Item.Power - x.Item.Power > 25 )
        .Where(x => x.CanChangeTag);
    if (reallyLowDupe.Any())
    {
        bestJunk.SetTag(NewTagKind.Infuse, "use to improve something *much* lower");
        infusionTargets.Add(GetSingleBest(reallyLowDupe));
    }
}

HashSet<string> classesEffected = new HashSet<string>();

foreach (var reason in appliedWeights.Where(x => x.TagChanged).GroupBy(x => x.NewTagReason))
{
    classesEffected.UnionWith(reason.Select(x => x.Item.Equippable).Distinct());
    Console.WriteLine(reason.Key);
    Console.WriteLine("  " + string.Join("\r\n  ", reason.Select(x => $"{x.Item.Name.PadRight(25)}    id:{x.Item.Id}")));
    Console.WriteLine(string.Join(" or ", reason.Select(x => $"id:{x.Item.Id}")));

}
Console.WriteLine();

foreach (var tag in appliedWeights.Where(x => x.TagChanged).GroupBy(x => x.NewTag.ToOldTagString()))
{
    classesEffected.UnionWith(tag.Select(x => x.Item.Equippable).Distinct());
    Console.WriteLine(tag.Key);
    Console.WriteLine(string.Join(" or ", tag.Select(x => $"id:{x.Item.Id}")));

}

if (infusionTargets.Any())
{
    classesEffected.UnionWith(infusionTargets.Select(x => x.Item.Equippable).Distinct());
    Console.WriteLine("infusion targets");
    Console.WriteLine("  " + string.Join("\r\n  ", infusionTargets.Select(x => $"{x.Item.Name.PadRight(25)}    id:{x.Item.Id}")));
    Console.WriteLine(string.Join(" or ", infusionTargets.Select(x => $"id:{x.Item.Id}")));
}

var longestName = weights.SelectMany(x => x.Value).MaxBy(x => x.Name.Length).Name.Length;

bool newThresholdSet = false;
Dictionary<string, Dictionary<Item, int>> considerDeleting = new Dictionary<string, Dictionary<Item, int>>();
Dictionary<string, float> findUpgrade = new Dictionary<string, float>();
foreach (var c in appliedWeights.Where(x => x.NewTag == NewTagKind.Keep).GroupBy(x => x.Item.Equippable))
{
    //if (!classesEffected.Contains(c.Key))
    //{
    //    continue;
    //}

    Console.WriteLine(c.Key);
    var weightSet = weights[c.Key];
    foreach (var set in weightSet)
    {
        string consoleLine = ("     " + set.Name).PadRight(longestName + 6);
        foreach (var type in c.GroupBy(x => x.Item.Type).OrderBy(x => ItemTypeComparer(x.Key)))
        {
            if (type.First().Item.IsClassItem)
            {
                continue;
            }

            var appliedSets = type.Where(x => x.Item.Tier != "Exotic").Select(x => x.Weights.Single(y => y.WeightSet == set));
            int count = appliedSets.Count(x => x.MeetsThreshold);
            float score = set.GetNormalizedThreshold(type.Key);
            consoleLine += ($"{count} ({score:F})").PadRight(15);
            if (set.ConsiderUpgrading)
            {
                findUpgrade.Add($"{c.Key} - {set.Name} - {type.Key}", score);
            }
            int excess = count - set.Count;
            if (excess > 0)
            {
                float newThreshold = appliedSets.Where(x => x.MeetsThreshold).OrderBy(x => x.Sum).Skip(excess).First().Sum;
                if (set.Threshold.Set(type.Key, newThreshold))
                {
                    newThresholdSet = true;
                    Console.WriteLine($"Setting the threshold for {c.Key} - {set.Name} - {type.Key} to {newThreshold}");
                }
            }
        }
        consoleLine += set.OverallNormalizedThreshold.ToString("F3");
        Console.WriteLine(consoleLine);
    }
}

Console.WriteLine("\r\nConsider upgrading:");
longestName = findUpgrade.Keys.MaxBy(x => x.Length).Length + 2;
foreach (var pair in findUpgrade.OrderBy(x => x.Value).Take(10))
{
    Console.WriteLine($"{pair.Key.PadRight(longestName)} : {pair.Value}");
}

if (newThresholdSet)
{
    string json = JsonConvert.SerializeObject(weights, Formatting.Indented);
    File.WriteAllText(weightsPath, json);
}

int ItemTypeComparer(string key)
{
    return key switch
    {
        "Helmet" => 1,
        "Festival Mask" => 1,
        "Gauntlets" => 2,
        "Chest Armor" => 3,
        "Leg Armor" => 4,
        "Hunter Cloak" => 5,
        "Titan Mark" => 5,
        "Warlock Bond" => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };
}

if (makeSpreadsheet)
{
    var outputRecords = appliedWeights
        .OrderBy(x => x.Item.Id)
        .Select(x => new
        {
            x.Item.Name,
            x.Item.Id,
            x.Item.Tag,
            x.NewTag,
            x.Item.Tier,
            x.Item.Type,
            x.Item.Equippable,
            x.Item.Power,
            x.Item.EnergyCapacityInt,
            x.Item.Mobility,
            x.Item.Resilience,
            x.Item.Recovery,
            x.Item.Discipline,
            x.Item.Intellect,
            x.Item.Strength,
            x.Item.SeasonalMod,
            SpecialPerks = string.Join(",", x.Item.SpecialPerks),
            x.AbsoluteValue,
            x.NewTagReason,
            FirstName = x.Weights.FirstOrDefault()?.WeightSet.Name,
            FirstSum = x.Weights.FirstOrDefault()?.Sum,
            SecondName = x.Weights.Skip(1).FirstOrDefault()?.WeightSet.Name,
            SecondSum = x.Weights.Skip(1).FirstOrDefault()?.Sum,
            ThirdName = x.Weights.Skip(2).FirstOrDefault()?.WeightSet.Name,
            ThirdSum = x.Weights.Skip(2).FirstOrDefault()?.Sum,
            FourthName = x.Weights.Skip(3).FirstOrDefault()?.WeightSet.Name,
            FourthSum = x.Weights.Skip(3).FirstOrDefault()?.Sum,
            FifthName = x.Weights.Skip(4).FirstOrDefault()?.WeightSet.Name,
            FifthSum = x.Weights.Skip(4).FirstOrDefault()?.Sum,
            SixthName = x.Weights.Skip(5).FirstOrDefault()?.WeightSet.Name,
            SixthSum = x.Weights.Skip(5).FirstOrDefault()?.Sum,
            SeventhName = x.Weights.Skip(6).FirstOrDefault()?.WeightSet.Name,
            SeventhSum = x.Weights.Skip(6).FirstOrDefault()?.Sum,
            EighthName = x.Weights.Skip(7).FirstOrDefault()?.WeightSet.Name,
            EighthSum = x.Weights.Skip(7).FirstOrDefault()?.Sum,
            FirstThreshold = x.Weights.FirstOrDefault()?.MeetsThreshold,
            SecondThreshold = x.Weights.Skip(1).FirstOrDefault()?.MeetsThreshold,
            ThirdThreshold = x.Weights.Skip(2).FirstOrDefault()?.MeetsThreshold,
            FourthThreshold = x.Weights.Skip(3).FirstOrDefault()?.MeetsThreshold,
            FifthThreshold = x.Weights.Skip(4).FirstOrDefault()?.MeetsThreshold,
            SixthThreshold = x.Weights.Skip(5).FirstOrDefault()?.MeetsThreshold,
            SeventhThreshold = x.Weights.Skip(6).FirstOrDefault()?.MeetsThreshold,
            EighthThreshold = x.Weights.Skip(7).FirstOrDefault()?.MeetsThreshold,
        });

    var outputPath = Path.Combine(@"C:\Users\geeve\Downloads", $"output-destinyArmor-{Guid.NewGuid()}.csv");
    using (var textWriter = new StreamWriter(outputPath))
    using (var writer = new CsvWriter(textWriter, config))
    {
        writer.WriteRecords(outputRecords);
    }
    Console.WriteLine($"Generated sheet: {outputPath}");
    Process.Start(@"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE", outputPath);
}

IEnumerable<ScratchPad> GetBest(IEnumerable<ScratchPad> records, int count)
{
    return records.OrderByDescending(x => x.AbsoluteValue).ThenByDescending(x => x.Item.EnergyCapacityInt).Take(count);
}

ScratchPad GetSingleBest(IEnumerable<ScratchPad> records)
{
    return GetBest(records, 1).Single();
}