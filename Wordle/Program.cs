﻿using System.Diagnostics;

string dictionaryFileName = "valid-wordle-words.txt";
string dictionaryPath = dictionaryFileName;
if (!File.Exists(dictionaryPath))
{
    dictionaryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dictionaryFileName);
}

string[] words = await File.ReadAllLinesAsync(dictionaryPath);
words = words
    .Select(x => x.ToLower().Trim())
    .Where(x => x.Length == 5)
    .Where(x => x == x.ToLower())
    .ToArray();

var wrongPlace = new HashSet<(char, int)>()
{
    //( 't', 0 ),
    //( 'a', 1 ),
    //( 'r', 2 ),
    //( 'e', 3 ),
    //( 's', 4 ),

    //( 'm', -1 ),
    //( 'n', 2 ),
    //( 'y', 4 ),
};

var rightPlace = new HashSet<(char, int)>()
{
    //{ 's', 0 },
    //{ 'h', 1 },
    //{ 'a', 2 },
    //{ 'e', 3 },
    //{ 'e', 4 },
};

var badLetters = new HashSet<char>()
{
    //'t',
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


string wordUsed = "slate";
Console.WriteLine($"Word used: [{wordUsed}]");
while (true)
{
    Console.WriteLine("Pattern returned: ('.' == miss, '-' == wrong place, 'x' == correct letter");
    string pattern = Console.ReadLine();
    Debug.Assert(pattern.Length == 5);
    Debug.Assert(pattern.All(x => x == '.' || x == '-' || x == 'x'));
    for (int ii = 0; ii < pattern.Length; ii++)
    {
        switch (pattern[ii])
        {
            case '.':
                badLetters.Add(wordUsed[ii]);
                break;
            case '-':
                wrongPlace.Add((wordUsed[ii], ii));
                break;
            case 'x':
                rightPlace.Add((wordUsed[ii], ii));
                break;
            default:
                Debug.Fail("What?");
                break;
        }
    }

    foreach (var pair in rightPlace)
    {
        words = words.Where(x => x[pair.Item2] == pair.Item1).ToArray();
    }

    foreach (var ch in badLetters)
    {
        words = words.Where(x =>
        {
            int offset = 0;
            foreach (var pair in rightPlace.OrderBy(a => a.Item2))
            {
                if (x[pair.Item2 - offset] == pair.Item1)
                {
                    x = x.Remove(pair.Item2 - offset, 1);
                    offset++;
                }
            }

            return !x.Contains(ch);
        }).ToArray();
    }

    foreach (var pair in wrongPlace)
    {
        words = words
            .Where(x => x.Remove(pair.Item2, 1).Contains(pair.Item1))
            .Where(x => x[pair.Item2] != pair.Item1)
            .ToArray();
    }

    Debug.Assert(words.Any());

    Dictionary<string, double> scoreLookup = new Dictionary<string, double>();
    foreach (string word in words)
    {
        scoreLookup[word] = words.Where(x => x != word).Sum(x => CompareWords(word, x));
        if (word.Distinct().Count() == word.Length)
        {
            scoreLookup[word] = Math.Round(scoreLookup[word] * 1.25, 3);
        }
    }

    scoreLookup = scoreLookup.OrderBy(x => x.Value).ToDictionary(x => x.Key, x => x.Value);
    var bestWords = scoreLookup.TakeLast(50);
    foreach (var word in bestWords)
    {
        Console.WriteLine($"{word.Key} : {word.Value}");
    }

    string bestWord = bestWords.Last().Key;
    Console.WriteLine($"Word used: [{bestWord}]");
    wordUsed = Console.ReadLine();
    if (string.IsNullOrEmpty(wordUsed))
    {
        wordUsed = bestWord;
    }
    Debug.Assert(wordUsed.Length == 5);
}




static int CompareWords(string guess, string answer)
{
    Debug.Assert(guess.Length == answer.Length);
    int score = 0;
    HashSet<char> wrongPlaceLetters = new HashSet<char>();
    for (int ii = 0; ii < guess.Length; ii++)
    {
        if (guess[ii] == answer[ii])
        {
            score += 3;
        }
        else if (answer.Contains(guess[ii]) && !wrongPlaceLetters.Contains(guess[ii]))
        {
            score += 2;
            wrongPlaceLetters.Add(guess[ii]);
        }
    }

    return score;
}