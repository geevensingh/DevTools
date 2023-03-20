
using System.Diagnostics.Metrics;

string dictionaryFileName = "valid-wordle-words.txt";
string[] words = await File.ReadAllLinesAsync(dictionaryFileName);
words = words
    .Select(x => x.ToLower().Trim())
    .Where(x => x.Length == 5)
    .Where(x => x == x.ToLower())
    .ToArray();

var exactPositionLookup = new Dictionary<char, Dictionary<int, HashSet<string>>>();
var alphabet = new char[] { 'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z' };

foreach (var letter in alphabet)
{
    exactPositionLookup[letter] = new Dictionary<int, HashSet<string>>();
    for (int index = 0; index < 5; index++)
    {
        exactPositionLookup[letter][index] = words.Where(x => x[index] == letter).ToHashSet();
    }
}

var wrongPositionLookup = new Dictionary<char, HashSet<string>>();
foreach (var letter in alphabet)
{
    wrongPositionLookup[letter] = words.Where(x => x.Contains(letter)).ToHashSet();
}

var scores = new Dictionary<string, int>();
foreach (var word in words)
{
    int score = 0;
    for (int index = 0; index < 5; index++)
    {
        score += exactPositionLookup[word[index]][index].Count / 5;

        if (word.Count(x => x == word[index]) > 1)
        {
            score += 0;
        }
        else
        {
            score += wrongPositionLookup[word[index]].Count;
        }
    }
    scores[word] = score;
}

int maxScore = scores.Max(x => x.Value);
foreach (var score in scores.Where(x => x.Value > maxScore * 0.9).OrderBy(x => x.Value))
{
    Console.WriteLine($"{score.Key}  :  {score.Value}");
}

Console.WriteLine();

foreach (var st in new string[] { "slate", "crane", "slant", "carte", "dream", "cares", })
{
    Console.WriteLine($"{st} is {scores[st].ToString().PadRight(10)}  ({scores[st] / (float)maxScore * 100.0})");
}

