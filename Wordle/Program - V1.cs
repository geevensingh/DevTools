
var wrongPlace = new HashSet<(char, int)>()
{
    //( 'c', 0 ),
    //( 'a', 1 ),
    //( 'r', 2 ),
    //( 'e', 3 ),
    //( 's', 4 ),

    //( 'm', -1 ),
    //( 'n', 2 ),
    //( 'y', 4 ),
};

var rightPlace = new Dictionary<char, int>()
{
    //{ 's', 0 },
    //{ 'h', 1 },
    //{ 'a', 2 },
    //{ 'e', 3 },
    //{ 'e', 4 },
};

var badLetters = new HashSet<char>()
{
    //'c',
    //'a',
    //'r',
    //'e',
    //'s',

    //'t',
    //'l',

    //'k',
    //'v',
    //'t',
    
    //'c',
    //'h',
    //'i',
    //'f',
    //'s',
    //'t',
    //'n',
};

string[] words = await File.ReadAllLinesAsync("Scrabble-Words.txt");
words = words
    .Select(x => x.ToLower().Trim())
    .Where(x => x.Length == 5)
    .Where(x => x == x.ToLower())
    .ToArray();

foreach (var pair in rightPlace)
{
    words = words.Where(x => x[pair.Value] == pair.Key).ToArray();
}

foreach (var ch in badLetters)
{
    words = words.Where(x => !x.Contains(ch)).ToArray();
}

foreach (var pair in wrongPlace)
{
    words = words.Where(x => x.Remove(pair.Item2, 1).Contains(pair.Item1)).ToArray();
}

Dictionary<int, Dictionary<char, int>> charCountLookup = new Dictionary<int, Dictionary<char, int>>();

for (int ii = 0; ii < 5; ii++)
{
    charCountLookup.Add(ii, words.Select(x => x[ii]).Distinct().ToDictionary(x => x, x => 0));
    foreach (char ch in charCountLookup[ii].Keys)
    {
        charCountLookup[ii][ch] = words.Count(x => x[ii] == ch);
    }

    int maxValueForChar = charCountLookup[ii].Max(x => x.Value);
    Console.WriteLine($"pos: {ii}    most frequent letter: {string.Join(",", charCountLookup[ii].Where(x => x.Value == maxValueForChar).Select(x => x.Key))}");
}

var wordLookup = new Dictionary<string, int>();
foreach (string word in words)
{
    int index = 0;
    int wordValue = 0;
    foreach (char ch in word)
    {
        wordValue += charCountLookup[index++][ch];
    }
    if (word.ToCharArray().Distinct().Count() == word.Length)
    {
        wordValue = (int)Math.Ceiling(wordValue * 1.0);
    }
    wordLookup.Add(word, wordValue);
}

foreach (var word in wordLookup.OrderBy(x => x.Value).Select(x => x.Key))
{
    Console.WriteLine($"{word} : {wordLookup[word]}");
}

Console.WriteLine();
int maxWordScore = wordLookup.Max(x => x.Value);
Console.WriteLine($"best word is '{string.Join(",", wordLookup.Where(x => x.Value == maxWordScore).Select(x => x.Key))}'");

Console.WriteLine();
