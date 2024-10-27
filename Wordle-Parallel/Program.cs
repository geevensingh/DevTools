using Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utilities;

namespace Wordle_Parallel
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            ConsoleLogger.Instance.MinLevel = LogLevel.Verbose;
            ConsoleLogger.Instance.IncludeTime = true;
            using ScopeLogger mainScope = new ScopeLogger();

            Debug.Assert(GetComparison("apple", "apple") == "22222");
            Debug.Assert(GetComparison("xppyz", "apple") == "02200");
            Debug.Assert(GetComparison("xyzpp", "apple") == "01100");
            Debug.Assert(GetComparison("xyzwp", "apple") == "01000");

            Debug.Assert(GetComparison("aaaaa", "abcde") == "20000");
            Debug.Assert(GetComparison("aaaaa", "bacde") == "02000");
            Debug.Assert(GetComparison("aaaaa", "bcade") == "00200");
            Debug.Assert(GetComparison("aaaaa", "bcdae") == "00020");
            Debug.Assert(GetComparison("aaaaa", "bcdea") == "00002");

            Debug.Assert(GetComparison("aaaaa", "aabcd") == "22000");
            Debug.Assert(GetComparison("aaaaa", "abacd") == "20200");
            Debug.Assert(GetComparison("aaaaa", "abcad") == "20020");
            Debug.Assert(GetComparison("aaaaa", "abcda") == "20002");

            Debug.Assert(GetComparison("zaazz", "abcde") == "10000");
            Debug.Assert(GetComparison("zaazz", "bacde") == "02000");
            Debug.Assert(GetComparison("zaazz", "bcade") == "00200");
            Debug.Assert(GetComparison("zaazz", "bcdae") == "00010");
            Debug.Assert(GetComparison("zaazz", "bcdea") == "00001");

            Debug.Assert(GetComparison("zaazz", "aabcd") == "12000");
            Debug.Assert(GetComparison("zaazz", "abacd") == "10200");
            Debug.Assert(GetComparison("zaazz", "abcad") == "10010");
            Debug.Assert(GetComparison("zaazz", "abcda") == "10001");

            Debug.Assert(GetComparison("zaazz", "baacd") == "02200");
            Debug.Assert(GetComparison("zaazz", "bacad") == "02010");
            Debug.Assert(GetComparison("zaazz", "bacda") == "02001");
            Debug.Assert(GetComparison("zaazz", "bcaad") == "00210");
            Debug.Assert(GetComparison("zaazz", "bcada") == "00201");
            Debug.Assert(GetComparison("zaazz", "bcdaa") == "00011");

            Debug.Assert(GetComparison("zaazz", "aaacd") == "02200");

            string[] words = File.ReadAllLines("wordle-words.txt");
            string[] answers = File.ReadAllLines("wordle-answers.txt");

            var guesses = words.Select(word => new Guess(word)).ToList();
            var tasks = guesses.Select(guess => guess.InitializeAsync(answers));
            await Task.WhenAll(tasks);

            int counter = 1;
            foreach (var guess in guesses.OrderByDescending(x => x.Bits))
            {
                Console.WriteLine($"{counter++}\t{guess.Word} - {guess.Entropy} - {guess.Bits} - keys: {guess.Groups.Count} , values: {guess.Groups.Values.Sum(x => x.Words.Count)}");
                if (guess.Word == "slant")
                {
                    break;
                }
            }

            ////var uberResults = new ConcurrentDictionary<string, Dictionary<string, HashSet<string>>>();
            ////var tasks = words.Select(word => GetComparisonsAsync(uberResults, answers, word));
            ////await Task.WhenAll(tasks);

            ////int counter = 1;
            ////foreach (var pair in uberResults.OrderByDescending(x => x.Value.Keys.Count))
            ////{
            ////    Console.WriteLine($"{counter++}\t{pair.Key} - keys: {pair.Value.Keys.Count} , values: {pair.Value.Values.Sum(x => x.Count)}");
            ////    if (pair.Key == "slant")
            ////    {
            ////        break;
            ////    }
            ////}
        }

        private static Task GetComparisonsAsync(ConcurrentDictionary<string, Dictionary<string, HashSet<string>>> uberResults, string[] answers, string word)
        {
            return Task.Run(() => {
                var results = new Dictionary<string, HashSet<string>>();
                foreach (var answer in answers)
                {
                    string comparison = GetComparison(answer, word);
                    if (!results.ContainsKey(comparison))
                    {
                        results[comparison] = new HashSet<string>();
                    }
                    results[comparison].Add(answer);
                }
                uberResults.TryAdd(word, results);
            });
        }

        public static string GetComparison(string answer, string guess)
        {
            var result = new string('0', answer.Length);
            Dictionary<char, int> alreadyCounted = answer.ToCharArray().Distinct().ToDictionary(ch => ch, ch => 0);
            for (int ii = 0; ii < answer.Length; ii++)
            {
                if (answer[ii] == guess[ii])
                {
                    Debug.Assert(result[ii] == '0');
                    result = result.ReplaceAtIndex(ii, '2');
                    alreadyCounted[answer[ii]]++;

                }
            }

            Dictionary<char, int> answerCharCounts = answer.ToCharArray().GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
            foreach (char ch in guess.ToCharArray().Distinct())
            {
                if (!answerCharCounts.ContainsKey(ch))
                {
                    continue;
                }

                int count = alreadyCounted[ch];
                for (int ii = 0; ii < guess.Length && count < answerCharCounts[ch]; ii++)
                {
                    if (guess[ii] == ch && result[ii] != '2')
                    {
                        Debug.Assert(result[ii] == '0');
                        result = result.ReplaceAtIndex(ii, '1');
                        count++;
                    }
                }
            }

            return result;
        }
    }
}
