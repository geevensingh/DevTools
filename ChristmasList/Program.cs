
using ChristmasList;

Dictionary<string, HashSet<string>> disallowed = new Dictionary<string, HashSet<string>>()
{
    { "Doug", new HashSet<string>() {"Geeven", "Nancy", "Tage", "Morgan" } },
    { "Mollie", new HashSet<string>() {"Nancy", "Morgan", "Doug", "Lise" } },
    { "Morgan", new HashSet<string>() { "Lise", "Geeven", "Mollie", "Tage" } },
    { "Lise", new HashSet<string>() {"Mollie", "Doug", "Morgan", "Nancy" } },
    { "Nancy", new HashSet<string>() {"Doug", "Tage", "Lise", "Geeven" } },
    { "Geeven", new HashSet<string>() {"Tage", "Mollie", "Nancy", "Doug" } },
    { "Tage", new HashSet<string>() {"Morgan", "Lise", "Geeven", "Mollie" } },
};

disallowed["Morgan"].Add("Nancy");
disallowed["Nancy"].Add("Morgan");

List<string> people = disallowed.Keys.ToList();

var combinations = GetPermutations(people, people.Count);
for (int ii = 0; ii < combinations.Count(); ii++)
{
    var combination = combinations[ii];
    Console.WriteLine(string.Join(", ", combination));

    CycleFinder cycleFinder = new CycleFinder(people, combination);
    if (cycleFinder.MinCycleLength < 3)
    {
        Console.WriteLine($"short cycle: {cycleFinder.MinCycleLength}");
        combinations.RemoveAt(ii--);
        continue;
    }

    for (int jj = 0; jj < combination.Count; jj++)
    {
        string giver = people[jj];
        string reciever = combination[jj];
        if (disallowed[giver].Contains(reciever))
        {
            Console.WriteLine($"{giver} can't give to {reciever}");
            combinations.RemoveAt(ii--);
            break;

        }
    }

}

Console.WriteLine("----------------");
Console.WriteLine($"combinations.Count() : {combinations.Count()}");
foreach (var combination in combinations)
{
    Console.WriteLine(string.Join(", ", combination));
}





static List<List<T>> GetPermutations<T>(IEnumerable<T> list, int length)
{
    if (length == 1) return list.Select(t => new List<T> { t }).ToList();
    return GetPermutations(list, length - 1)
        .SelectMany(t => list.Where(o => !t.Contains(o)),
            (t1, t2) =>
            {
                return t1.Concat(new T[] { t2 }).ToList();
            }).ToList();
}