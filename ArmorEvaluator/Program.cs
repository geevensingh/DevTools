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

var toBeEvaluated = appliedWeights
    .Where(x => !x.MeetsThreshold)
    .Where(x => x.Item.MasterworkTier < 10)
    .Where(x => x.Item.Tag != "favorite")
    .ToHashSet(comparer);

// Assume that everything is junk to start
foreach (var item in toBeEvaluated) { item.NewTag = "junk"; }

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
                modType.MaxBy(x => x.AbsoluteValue).NewTag = "keep";
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
        items.MaxBy(x => x.AbsoluteValue).NewTag = "keep";
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
            foreach (var item in toInfuse) { item.NewTag = "infuse"; }
        }
    }
}

// Mark for infusion all junk with a lower power masterwork dupe
foreach (var name in toBeEvaluated.Where(x => x.IsJunk).GroupBy(x => x.Item.Name))
{
    var bestJunk = name.MaxBy(x => x.Item.Power);
    var masterworkDupe = appliedWeights
        .Where(x => x.Item.Name == name.Key)
        .Where(x => x.Item.Power < bestJunk.Item.Power)
        .Where(x => x.Item.MasterworkTier == 10);
    if (masterworkDupe.Any())
    {
        bestJunk.NewTag = "infuse";
    }
}

foreach (var tag in toBeEvaluated.Where(x => x.TagChanged).GroupBy(x => x.NewTag))
{
    Console.WriteLine(tag.Key);
    Console.WriteLine(string.Join(" or ", tag.Select(x => $"id:{x.Item.Id}")));

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