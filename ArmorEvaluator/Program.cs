// See https://aka.ms/new-console-template for more information

using ArmorEvaluator;
using CsvHelper;
using CsvHelper.Configuration;
using System.Diagnostics;
using System.Globalization;

var config = new CsvConfiguration(CultureInfo.InvariantCulture)
{
    PrepareHeaderForMatch = args => args.Header.Replace(" ", string.Empty),
};

string filePath = Directory.EnumerateFiles(Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE"), @"Downloads"), @"destinyArmor*.csv").MaxBy(x => File.GetCreationTime(x));
Console.WriteLine($"Using {filePath}");

IEnumerable<Item> allItems;
using (var reader = new StreamReader(filePath))
using (var csv = new CsvReader(reader, config))
{
    allItems = csv.GetRecords<Item>()
        .Where(x => x.Tier != "Rare")
        .Where(x => x.Type != "Hunter Cloak")
        .Where(x => x.Type != "Titan Mark")
        .Where(x => x.Type != "Warlock Bond")
        .ToList();
}

var weights = WeightSet.GetDefaults();

var comparer = new Comparer();
var appliedWeights = allItems.Select(item => new ScratchPad(item, AppliedWeightSet.Create(item, weights[item.Equippable]))).ToHashSet(comparer);

// Assume we're keeping everything
foreach (var item in appliedWeights
    .Where(x => x.MeetsThreshold || x.Item.MasterworkTier == 10)
    .Where(x => x.Item.Tag != "favorite"))
{ item.SetTag("keep", "default"); }

var toBeEvaluated = appliedWeights
    .Where(x => !x.MeetsThreshold)
    .Where(x => x.Item.MasterworkTier < 10)
    .Where(x => x.Item.Tag != "favorite")
    .ToHashSet(comparer);

// Assume that everything is junk to start
foreach (var item in toBeEvaluated) { item.SetTag("junk", "default"); }

// Make sure we don't delete everything with a specific seasonal mod slot
var allJunk = toBeEvaluated.Where(x => x.IsJunk);
foreach (var c in appliedWeights.GroupBy(x => x.Item.Equippable))
{
    foreach (var type in c.GroupBy(x => x.Item.Type))
    {
        foreach (var modType in type.GroupBy(x => x.Item.SeasonalMod))
        {
            if (modType.All(x => allJunk.Contains(x)))
            {
                modType.MaxBy(x => x.AbsoluteValue).SetTag("keep", $"best {c.Key} {type.Key} {modType.Key}");
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
        items.MaxBy(x => x.AbsoluteValue).SetTag("keep", $"best exotic {name}");
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
        .Where(x => x.Item.MasterworkTier == 10);
    if (masterworkDupe.Any())
    {
        bestJunk.SetTag("infuse", "use to improve a masterwork");
        infusionTargets.Add(masterworkDupe.MaxBy(x => x.AbsoluteValue));
    }
}

// Mark for infusion all junk with a MUCH lower power dupe
foreach (var hash in toBeEvaluated.Where(x => x.IsJunk).GroupBy(x => x.Item.Hash))
{
    var bestJunk = hash.MaxBy(x => x.Item.Power);
    var reallyLowDupe = appliedWeights
        .Where(x => x.Item.Hash == hash.Key)
        .Where(x => bestJunk.Item.Power - x.Item.Power > 20 )
        .Where(x => x.NewTag != "junk");
    if (reallyLowDupe.Any())
    {
        bestJunk.SetTag("infuse", "use to improve something *much* lower");
        infusionTargets.Add(reallyLowDupe.MaxBy(x => x.AbsoluteValue));
    }
}

// Look for items that are strictly worse that others
foreach (var eval in appliedWeights.Where(x => x.Item.Tier == "Legendary"))
{
    var strictlyBetter = appliedWeights
        .Where(x => x.Item.Id != eval.Item.Id)
        .Where(x => x.Item.Tier != "Exotic")
        .Where(x => x.Item.Type == eval.Item.Type)
        .Where(x => x.Item.Equippable == eval.Item.Equippable)
        .Where(x => x.Item.SeasonalMod == eval.Item.SeasonalMod)
        .Where(x => x.Item.Total > eval.Item.Total)
        .Where(x => x.Item.Mobility >= eval.Item.Mobility)
        .Where(x => x.Item.Resilience >= eval.Item.Resilience)
        .Where(x => x.Item.Recovery >= eval.Item.Recovery)
        .Where(x => x.Item.Discipline >= eval.Item.Discipline)
        .Where(x => x.Item.Intellect >= eval.Item.Intellect)
        .Where(x => x.Item.Strength >= eval.Item.Strength);
    if (strictlyBetter.Count() > 0)
    {
        eval.SetTag("junk", "strictly worse than other");
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


if (false)
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
            x.Item.MasterworkTier,
            x.Item.Mobility,
            x.Item.Resilience,
            x.Item.Recovery,
            x.Item.Discipline,
            x.Item.Intellect,
            x.Item.Strength,
            x.Item.SeasonalMod,
            x.AbsoluteValue,
            FirstName = x.Weights.FirstOrDefault()?.WeightSet.Name,
            FirstSum = x.Weights.FirstOrDefault()?.Sum,
            SecondName = x.Weights.Skip(1).FirstOrDefault()?.WeightSet.Name,
            SecondSum = x.Weights.Skip(1).FirstOrDefault()?.Sum,
            ThirdName = x.Weights.Skip(2).FirstOrDefault()?.WeightSet.Name,
            ThirdSum = x.Weights.Skip(2).FirstOrDefault()?.Sum,
        });

    var outputPath = Path.Combine(@"C:\Users\geeve\Downloads", $"destinyArmor-{Guid.NewGuid()}.csv");
    using (var textWriter = new StreamWriter(outputPath))
    using (var writer = new CsvWriter(textWriter, config))
    {
        writer.WriteRecords(outputRecords);
    }

    Process.Start(@"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE", outputPath);
}

HashSet<ScratchPad> GetStrictlyBetter(ScratchPad given, IEnumerable<ScratchPad> everything)
{
    return everything
        .Where(x => x.Item.Id != given.Item.Id)
        .Where(x => x.Item.Tier != "Exotic")
        .Where(x => x.Item.Type == given.Item.Type)
        .Where(x => x.Item.Equippable == given.Item.Equippable)
        .Where(x => x.Item.SeasonalMod == given.Item.SeasonalMod)
        .Where(x => x.Item.Total > given.Item.Total)
        .Where(x => x.Item.Mobility >= given.Item.Mobility)
        .Where(x => x.Item.Resilience >= given.Item.Resilience)
        .Where(x => x.Item.Recovery >= given.Item.Recovery)
        .Where(x => x.Item.Discipline >= given.Item.Discipline)
        .Where(x => x.Item.Intellect >= given.Item.Intellect)
        .Where(x => x.Item.Strength >= given.Item.Strength)
        .ToHashSet();
}
