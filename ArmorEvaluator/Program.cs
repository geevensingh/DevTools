// See https://aka.ms/new-console-template for more information

using ArmorEvaluator;
using CsvHelper;
using CsvHelper.Configuration;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Globalization;

string exePath = AppDomain.CurrentDomain.BaseDirectory;
string weightsPath = Path.Combine(exePath, "Weights.json");

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
foreach (var item in appliedWeights
    .Where(x => x.MeetsThresholdOrIsSpecial || x.Item.MasterworkTierInt == 10)
    .Where(x => x.Item.Tag != "favorite"))
{ item.SetTag("keep", "meets threshold or masterwork"); }

var toBeEvaluated = appliedWeights
    .Where(x => x.Item.MasterworkTierInt < 10)
    .Where(x => x.Item.Tag != "favorite")
    .Where(x => !x.Item.IsClassItem)
    .ToHashSet(comparer);

// Assume that everything is junk to start
foreach (var item in toBeEvaluated.Where(x => !x.MeetsThresholdOrIsSpecial)) { item.SetTag("junk", "does not meet threshold or masterwork"); }

// For titan and hunter, keep only the best few in each slot
foreach (var c in appliedWeights.GroupBy(x => x.Item.Equippable).Where(x => x.Key == "Hunter" || x.Key == "Titan"))
{
    int count = 3;
    if (c.Key == "Hunter")
    {
        count = 6;
    }

    foreach (var type in c.Where(x => x.Item.Tier != "Exotic").GroupBy(x => x.Item.Type))
    {
        var best = GetBest(type, count);
        var garbage = type.Where(x => !best.Contains(x));
        foreach (var item in garbage) { item.SetTag("junk", $"Not high enough for {c.Key} - didn't make the top {count}"); }
    }
}

// Look for items that are strictly worse that others
var evaluated = new HashSet<ScratchPad>();
foreach (var eval in appliedWeights.Where(x => x.Item.Tier == "Legendary" && !x.Item.IsClassItem))
{
    if (evaluated.Contains(eval))
    {
        continue;
    }

    var dupeSet = appliedWeights
        .Where(x => x.Item.Equippable == eval.Item.Equippable)
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
        evaluated.UnionWith(dupeSet);
        var bestDupe = GetSingleBest(dupeSet);
        foreach (var dupe in dupeSet.Where(x => x.Item.MasterworkTierInt < 10 && x.Item.Tag != "favorite"))
        {
            if (dupe != bestDupe)
            {
                dupe.SetTag("junk", $"strictly worse than {bestDupe.Item.Name}");
            }
        }
    }
}

// Make sure we don't delete everything with a specific seasonal mod slot
var allJunk = toBeEvaluated.Where(x => x.IsJunk);
foreach (var c in appliedWeights.GroupBy(x => x.Item.Equippable))
{
    foreach (var type in c.GroupBy(x => x.Item.Type))
    {
        foreach (var modType in type.GroupBy(x => x.Item.UniqueType))
        {
            if (modType.All(x => allJunk.Contains(x)))
            {
                GetSingleBest(modType).SetTag("keep", $"best {c.Key} {type.Key} {modType.Key}");
            }
        }
    }
}

// Make sure we don't delete all of a given exotic
allJunk = toBeEvaluated.Where(x => x.IsJunk);
foreach (var name in allJunk.Where(x => x.Item.Tier == "Exotic").Select(x => x.Item.Name).Distinct())
{
    var items = appliedWeights.Where(x => x.Item.Name == name);
    if (items.All(x => allJunk.Contains(x)))
    {
        GetSingleBest(items).SetTag("keep", $"best exotic {name}");
    }
}

// Make sure we don't delete the highest power in any slot
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
            foreach (var item in toInfuse) { item.SetTag("infuse", $"highest power in {c.Key} {type.Key}"); }
        }
    }
}

// Mark for infusion all junk with a lower power masterwork dupe
var infusionTargets = new HashSet<ScratchPad>();
foreach (var hash in toBeEvaluated.Where(x => x.IsJunk).GroupBy(x => x.Item.Hash))
{
    var bestJunk = hash.MaxBy(x => x.Item.Power);
    var masterworkDupe = appliedWeights
        .Where(x => x.Item.Hash == hash.Key)
        .Where(x => x.Item.Power < bestJunk.Item.Power)
        .Where(x => x.Item.MasterworkTierInt == 10);
    if (masterworkDupe.Any())
    {
        bestJunk.SetTag("infuse", "use to improve a masterwork");
        infusionTargets.Add(GetSingleBest(masterworkDupe));
    }
}

// Mark for infusion all junk with a MUCH lower power dupe
foreach (var hash in toBeEvaluated.Where(x => x.IsJunk).GroupBy(x => x.Item.Hash))
{
    var bestJunk = hash.MaxBy(x => x.Item.Power);
    var reallyLowDupe = appliedWeights
        .Where(x => x.Item.Hash == hash.Key)
        .Where(x => bestJunk.Item.Power - x.Item.Power > 25 )
        .Where(x => x.NewTag != "junk");
    if (reallyLowDupe.Any())
    {
        bestJunk.SetTag("infuse", "use to improve something *much* lower");
        infusionTargets.Add(GetSingleBest(reallyLowDupe));
    }
}

foreach (var reason in appliedWeights.Where(x => x.TagChanged).GroupBy(x => x.NewTagReason))
{
    Console.WriteLine(reason.Key);
    Console.WriteLine("  " + string.Join("\r\n  ", reason.Select(x => x.Item.Name)));
    Console.WriteLine(string.Join(" or ", reason.Select(x => $"id:{x.Item.Id}")));

}
Console.WriteLine();

foreach (var tag in appliedWeights.Where(x => x.TagChanged).GroupBy(x => x.NewTag))
{
    Console.WriteLine(tag.Key);
    Console.WriteLine(string.Join(" or ", tag.Select(x => $"id:{x.Item.Id}")));

}

if (infusionTargets.Any())
{
    Console.WriteLine("infusion targets");
    Console.WriteLine("  " + string.Join("\r\n  ", infusionTargets.Select(x => x.Item.Name)));
    Console.WriteLine(string.Join(" or ", infusionTargets.Select(x => $"id:{x.Item.Id}")));
}

var longestName = weights.SelectMany(x => x.Value).MaxBy(x => x.Name.Length).Name.Length;

bool newThresholdSet = false;
Dictionary<string, Dictionary<Item, int>> considerDeleting = new Dictionary<string, Dictionary<Item, int>>();
foreach (var c in appliedWeights.Where(x => x.NewTag == "keep").GroupBy(x => x.Item.Equippable))
{
    Console.WriteLine(c.Key);
    var weightSet = weights[c.Key];
    foreach (var set in weightSet)
    {
        string consoleLine = ("     " + set.Name).PadRight(longestName + 5);
        foreach (var type in c.GroupBy(x => x.Item.Type).OrderBy(x => ItemTypeComparer(x.Key)))
        {
            if (type.First().Item.IsClassItem)
            {
                continue;
            }

            var appliedSets = type.Where(x => x.Item.Tier != "Exotic").Select(x => x.Weights.Single(y => y.WeightSet == set));
            int count = appliedSets.Count(x => x.MeetsThreshold);
            consoleLine += ("  " + count).PadRight(6);
            int excess = count - set.Count;
            if (excess > 0)
            {
                float newThreshold = appliedSets.Where(x => x.MeetsThreshold).OrderBy(x => x.Sum).Skip(excess).First().Sum;
                newThresholdSet = set.Threshold.Set(type.Key, newThreshold);
                if (newThresholdSet)
                {
                    Console.WriteLine($"Setting the threshold for {c.Key} - {set.Name} - {type.Key} to {newThreshold}");
                }
            }
        }
        Console.WriteLine(consoleLine);
    }
}

if (newThresholdSet)
{
    string json = JsonConvert.SerializeObject(weights);
    File.WriteAllText(weightsPath, json);
}

int ItemTypeComparer(string key)
{
    return key switch
    {
        "Helmet" => 1,
        "Gauntlets" => 2,
        "Chest Armor" => 3,
        "Leg Armor" => 4,
        "Hunter Cloak" => 5,
        "Titan Mark" => 6,
        "Warlock Bond" => 7,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };
}

if (makeSpreadsheet)
{
    var outputRecords = appliedWeights
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
            x.Item.MasterworkTierInt,
            x.Item.Mobility,
            x.Item.Resilience,
            x.Item.Recovery,
            x.Item.Discipline,
            x.Item.Intellect,
            x.Item.Strength,
            x.Item.SeasonalMod,
            SpecialPerks = string.Join(",", x.Item.SpecialPerks),
            x.AbsoluteValue,
            FirstName = x.Weights.FirstOrDefault()?.WeightSet.Name,
            FirstSum = x.Weights.FirstOrDefault()?.Sum,
            SecondName = x.Weights.Skip(1).FirstOrDefault()?.WeightSet.Name,
            SecondSum = x.Weights.Skip(1).FirstOrDefault()?.Sum,
            ThirdName = x.Weights.Skip(2).FirstOrDefault()?.WeightSet.Name,
            ThirdSum = x.Weights.Skip(2).FirstOrDefault()?.Sum,
            FourthName = x.Weights.Skip(3).FirstOrDefault()?.WeightSet.Name,
            FourthSum = x.Weights.Skip(3).FirstOrDefault()?.Sum,
            FirstThreshold = x.Weights.FirstOrDefault()?.MeetsThreshold,
            SecondThreshold = x.Weights.Skip(1).FirstOrDefault()?.MeetsThreshold,
            ThirdThreshold = x.Weights.Skip(2).FirstOrDefault()?.MeetsThreshold,
            FourthThreshold = x.Weights.Skip(3).FirstOrDefault()?.MeetsThreshold,
        });

    var outputPath = Path.Combine(@"C:\Users\geeve\Downloads", $"output-destinyArmor-{Guid.NewGuid()}.csv");
    using (var textWriter = new StreamWriter(outputPath))
    using (var writer = new CsvWriter(textWriter, config))
    {
        writer.WriteRecords(outputRecords);
    }

    Process.Start(@"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE", outputPath);
}

IEnumerable<ScratchPad> GetBest(IEnumerable<ScratchPad> records, int count)
{
    return records.OrderByDescending(x => x.AbsoluteValue).ThenByDescending(x => x.Item.MasterworkTierInt).Take(count);
}

ScratchPad GetSingleBest(IEnumerable<ScratchPad> records)
{
    return GetBest(records, 1).Single();
}