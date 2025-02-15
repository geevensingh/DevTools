namespace LetterBoxed
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    internal class Program
    {
        static void Main(string[] args)
        {
            HashSet<string> words = new HashSet<string>(File.ReadAllLines("words_alpha.txt").Where(x => x.Length > 3).Select(x => x.ToLower()));
            var sides = new char[][]
            {
                "rwg".ToCharArray(),
                "one".ToCharArray(),
                "fsh".ToCharArray(),
                "plk".ToCharArray(),
            };
            var allLetters = sides.SelectMany(x => x);
            words = words.Where(x => x.ToCharArray().All(y => allLetters.Contains(y))).ToHashSet();

            Dictionary<string, HashSet<char>> validWords = new Dictionary<string, HashSet<char>>();
            foreach (var word in words)
            {
                bool valid = true;
                int lastSideIndex = -1;
                HashSet<char> lettersUsed = new HashSet<char>();
                for (int ii = 0; ii < word.Length; ii++)
                {
                    int sideIndex = IndexOf(sides, word[ii]);
                    Debug.Assert(sideIndex >= 0);
                    if (lastSideIndex == sideIndex)
                    {
                        valid = false;
                        break;
                    }
                    lastSideIndex = sideIndex;
                    lettersUsed.Add(word[ii]);
                }

                if (valid)
                {
                    validWords[word] = lettersUsed;
                }
            }

            var results = validWords.Keys.OrderBy(x => validWords[x].Count);
            //results = results.Where(x => x[0] == 'm' && validWords[x].Contains('z') && validWords[x].Contains('c') && validWords[x].Contains('n')).OrderBy(x => validWords[x].Count);
            foreach (var result in results)
            {
                Console.WriteLine($"{validWords[result].Count}\t{result.PadRight(15)}\t\t{string.Join(",",allLetters.Except(validWords[result]))}");
            }

            var chains = new Dictionary<List<string>, HashSet<char>>();
            foreach (var validWord in validWords)
            {
                chains[new List<string> { validWord.Key }] = allLetters.Except(validWord.Value).ToHashSet();
            }

            int resultCount = 0;
            int chainDepth = 1;
            while (resultCount == 0)
            {
                Console.WriteLine($"Chains: {chainDepth}");
                chains = NextStep(chains, validWords);
                chainDepth++;
                resultCount = chains.Count(x => !x.Value.Any());
                Console.WriteLine($"Found: {resultCount}");
            }

            ////var remainingWords = validWords.Where(x => x.Key[0] == validWord.Key[0] && missingLetters.Intersect(x.Value).Any());
            ////foreach (var word in remainingWords)
            ////{
            ////    chains[new List<string> { validWord.Key, word.Key }] = allLetters.Except(validWord.Value.Union(word.Value)).ToHashSet();
            ////}

            ////foreach (var validWord in validWords)
            ////{
            ////    var chain = new List<string> { validWord.Key };
            ////    var lettersUsed = validWord.Value;
            ////    var missingLetters = allLetters.Except(lettersUsed);
            ////    var remainingWords = validWords.Where(x => x.Key[0] == validWord.Key[0] && missingLetters.Intersect(x.Value).Count() == missingLetters.Count());
            ////    foreach (var word in remainingWords)
            ////    {
            ////        chains.Add(new List<string> { validWord.Key, word.Key });
            ////    }
            ////}

            foreach (var chain in chains.Where(x => !x.Value.Any()))
            {
                Console.WriteLine(string.Join(" ", chain.Key));
            }
        }

        private static Dictionary<List<string>, HashSet<char>> NextStep(Dictionary<List<string>, HashSet<char>> chains, Dictionary<string, HashSet<char>> validWords)
        {
            var newChains = new Dictionary<List<string>, HashSet<char>>();
            foreach (var existingChain in chains)
            {
                if (existingChain.Value.Count == 0)
                {
                    newChains[existingChain.Key] = existingChain.Value;
                    continue;
                }

                var helpfulWords = validWords.Where(x => x.Key[0] == existingChain.Key.Last().ToCharArray().Last() && existingChain.Value.Intersect(x.Value).Any());
                foreach (var item in helpfulWords)
                {
                    var missingLetters = existingChain.Value.Except(item.Value).ToHashSet();
                    var newKey = existingChain.Key.Append(item.Key).ToList();
                    newChains[newKey] = missingLetters;
                }
            }

            return newChains;
        }

        public static int IndexOf<T>(T[][] matrix, T value)
        {
            for (int ii = 0; ii < matrix.Length; ii++)
            {
                if (matrix[ii].Contains(value))
                {
                    return ii;
                }
            }
            return -1;
        }
    }
}
