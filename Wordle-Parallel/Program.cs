using Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Utilities;

namespace Wordle_Parallel
{
    public class Program
    {
        private static float Sigmoid(float x)
        {
            double exp = 1.000005d;
            double offset = 70000.0d;
            return (float)(1.0 / (1.0 + Math.Pow(exp, offset - x)));
        }

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

            Dictionary<string, float> answerChance = await GenerateAnswerChance();

            //char[] badLetters = "slantcodeugby".ToCharArray();
            //List<string> answersTemp = answerChance.Keys.ToList();
            //answersTemp = answersTemp.Where(x => !ContainsAny(x, badLetters)).ToList();
            //Debug.Assert(!answersTemp.Contains("there"));
            //answersTemp = answersTemp.Where(x => x.Contains("r") && x[0] != 'r' && x[4] != 'r').ToList();
            //answersTemp.ForEach(x => Console.WriteLine($"{x}\t:\t{answerChance[x]}"));

            //answerChance = answerChance.OrderByDescending(x => x.Value).Take(5000).ToDictionary(x => x.Key, x => x.Value);

            float minThreshold = 0.91f;
            minThreshold = 0.5010774f;
            int maxAnswers = int.MaxValue;
            Console.WriteLine("possible answers : " + answerChance.Count(x => x.Value > minThreshold));

            HashSet<string> answers = answerChance.OrderByDescending(x => x.Value).Where(x => x.Value > minThreshold).Take(maxAnswers).Select(x => x.Key).ToHashSet();
            int maxWords = answers.Count * 4;
            HashSet<string> words = answerChance.OrderByDescending(x => x.Value).Take(maxWords).Select(x => x.Key).ToHashSet();

            //HashSet<string> answers = answerChance.OrderByDescending(x => x.Value).Take(1500).Select(x => x.Key).ToHashSet();
            Logger.Instance.LogLine($"words: {words.Count}, answers: {answers.Count}");

            foreach (var word in new string[] { "talky", "tally"})
            {
                Console.WriteLine($"{word} : {answerChance[word]}");
            }

#if false
            List<Guess> guesses = await GenerateAllGuesses(words, answers);
            WriteGuesses(guesses);
            foreach (var example in new[] {
                "plate",
                "plane",
                "trope",
                "place",
                "plant",
                "caret",
                "dealt",
                "table",
                "trace",
                "crane",
                "carte",
                "split",
                "leapt",
                "slice",
                "slate",
                "slant",
                "salet"
            })
            {
                try
                {
                    Logger.Instance.LogLine($"index of '{example}': {guesses.IndexOf(guesses.Single(x => x.Word == example))}");
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogLine($"error finding '{example}': {ex.Message}", LogLevel.Error);
                }
            }
            Guess bestWord = guesses.First(x => x.IsAnswer);
            Logger.Instance.LogLine($"best valid answer is '{bestWord.Word}'");
            Logger.Instance.LogLine($"index of '{bestWord.Word}': {guesses.IndexOf(bestWord)}");
#endif

            //
            // Second step
            //

            string firstWord = "slant";
            //string firstResponse = "01101";
            GetInput("first response", out string firstResponse);
            //var firstGroup = guesses.First(x => x.Word == firstWord).Groups[firstResponse];
            if (TryReadGuess(answers.Count, firstWord, out Guess firstGuess, out _))
            {
                Logger.Instance.LogLine($"loaded existing guess for '{firstWord}'");
            }
            else
            {
                Logger.Instance.LogLine($"failed to load existing guess for '{firstWord}'", LogLevel.Error);
                firstGuess = new Guess(firstWord, answers.Contains(firstWord));
                firstGuess = (await GenerateAllGuesses(new string[] { firstWord }, answers)).Single();
            }


            var firstGroup = firstGuess.Groups[firstResponse];
            answers = firstGroup.Words;

            List<Guess> guesses = null;
            {
                using var logScope = new ScopeLogger("building second guesses");
                guesses = words.Select(word => new Guess(word, answers.Contains(word))).ToList();
                await Task.WhenAll(guesses.Select(guess => guess.InitializeAsync(answers)).ToList());
            }
            guesses = guesses.OrderByDescending(x => x.GetEntropy()).ToList();
            WriteGuesses(guesses);

            //
            // third step
            //

            GetInput("second word", out string secondWord, guesses.First(x => x.IsAnswer).Word);
            GetInput("second response", out string secondResponse);
            //string secondWord = "latte";
            //string secondResponse = "12100";
            var secondGroup = guesses.Single(x => x.Word == secondWord).Groups[secondResponse];
            answers = secondGroup.Words;

            {
                using var logScope = new ScopeLogger("building third guesses");
                guesses = words.Select(word => new Guess(word, answers.Contains(word))).ToList();
                await Task.WhenAll(guesses.Select(guess => guess.InitializeAsync(answers)).ToList());
            }
            guesses = guesses.OrderByDescending(x => x.GetEntropy()).ToList();
            WriteGuesses(guesses);

            //
            // fourth step
            //

            GetInput("third word", out string thirdWord, guesses.First(x => x.IsAnswer).Word);
            GetInput("third response", out string thirdResponse);
            var thirdGroup = guesses.Single(x => x.Word == thirdWord).Groups[thirdResponse];
            answers = thirdGroup.Words;

            {
                using var logScope = new ScopeLogger("building fourth guesses");
                guesses = words.Select(word => new Guess(word, answers.Contains(word))).ToList();
                await Task.WhenAll(guesses.Select(guess => guess.InitializeAsync(answers)).ToList());
            }
            guesses = guesses.OrderByDescending(x => x.GetEntropy()).ToList();
            WriteGuesses(guesses);

            //
            // fifth step
            //

            GetInput("fourth word", out string fourthWord, guesses.First(x => x.IsAnswer).Word);
            GetInput("fourth response", out string fourthResponse);
            var fourthGroup = guesses.Single(x => x.Word == fourthWord).Groups[fourthResponse];
            answers = fourthGroup.Words;

            {
                using var logScope = new ScopeLogger("building fifth guesses");
                guesses = words.Select(word => new Guess(word, answers.Contains(word))).ToList();
                await Task.WhenAll(guesses.Select(guess => guess.InitializeAsync(answers)).ToList());
            }
            guesses = guesses.OrderByDescending(x => x.GetEntropy()).ToList();
            WriteGuesses(guesses);

            //
            // sixth step
            //

            GetInput("fifth word", out string fifthWord, guesses.First(x => x.IsAnswer).Word);
            GetInput("fifth response", out string fifthResponse);
            var fifthGroup = guesses.Single(x => x.Word == fifthWord).Groups[fifthResponse];
            answers = fifthGroup.Words;

            {
                using var logScope = new ScopeLogger("building sixth guesses");
                guesses = words.Select(word => new Guess(word, answers.Contains(word))).ToList();
                await Task.WhenAll(guesses.Select(guess => guess.InitializeAsync(answers)).ToList());
            }
            guesses = guesses.OrderByDescending(x => x.GetEntropy()).ToList();
            WriteGuesses(guesses);

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


        private static void GetInput(string name, out string response, string defaultResponse = null)
        {
            if (string.IsNullOrEmpty(defaultResponse))
            {
                Console.WriteLine($"Enter {name}: ");
            }
            else
            {
                Console.WriteLine($"Enter {name} (default: {defaultResponse}): ");
            }
            response = Console.ReadLine();
            if (string.IsNullOrEmpty(response) && !string.IsNullOrEmpty(defaultResponse))
            {
                response = defaultResponse;
            }
        }

        private static async Task<Dictionary<string, float>> GenerateAnswerChance()
        {
            bool readFrequenciesFromFile = false;
            Dictionary<string, float> answerChance = null;
            if (File.Exists("wordle-frequencies.json"))
            {
                using var logScope = new ScopeLogger("reading existing frequencies");
                using var jsonReader = new JsonTextReader(new StringReader(File.ReadAllText("wordle-frequencies.json")));
                var serializer = JsonSerializer.Create();
                try
                {
                    answerChance = serializer.Deserialize<Dictionary<string, float>>(jsonReader);
                    readFrequenciesFromFile = true;
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogLine($"Error deserializing JSON: {ex.Message}", LogLevel.Error);
                }
            }

            if (answerChance == null)
            {
                using var logScope = new ScopeLogger("building frequencies");

                Dictionary<string, long> answerFrequency = File.ReadAllLines("wordle-answers.txt").ToDictionary((string x) => x, (string x) => -1L);
                string[] lines = File.ReadAllLines(@"C:\Users\geeve\Downloads\frequency-alpha-alldicts.txt");
                var wordFrequency = new Dictionary<string, long>();
                foreach (string line in lines)
                {
                    string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    Debug.Assert(parts.Length == 5);
                    string word = parts[1];
                    if (word.Length != 5)
                    {
                        continue;
                    }

                    word = word.ToLower();
                    int raking = int.Parse(parts[0]);
                    long count = long.Parse(parts[2].Replace(",", ""));
                    wordFrequency[word] = count;
                    //Console.WriteLine($"{raking}\t{word}\t{count}\t");

                    if (answerFrequency.ContainsKey(word))
                    {
                        answerFrequency[word] = count;
                    }
                }

                long minAnswerFrequency = answerFrequency.Where(x => x.Value > 0L).Min(x => x.Value);
                foreach (var pair in answerFrequency.Where(x => x.Value <= 0L).ToList())
                {
                    answerFrequency[pair.Key] = minAnswerFrequency;
                }

                Logger.Instance.LogLine($"sigmoid of {minAnswerFrequency} (min answer) is {Sigmoid(minAnswerFrequency)}", LogLevel.Verbose);

                string[] words = File.ReadAllLines("wordle-words.txt");
                long minWordFrequency = wordFrequency.Min(x => x.Value);
                foreach (string w in words)
                {
                    if (!wordFrequency.ContainsKey(w))
                    {
                        //Console.WriteLine($"'{w}' unknown frequency");
                        wordFrequency[w] = minWordFrequency;
                    }
                }

                Logger.Instance.LogLine($"sigmoid of {minWordFrequency} (min word) is {Sigmoid(minWordFrequency)}", LogLevel.Verbose);

                // make sure *everything* is in wordFrequency
                foreach (var pair in answerFrequency)
                {
                    wordFrequency[pair.Key] = pair.Value;
                }

                answerChance = new Dictionary<string, float>();
                foreach (var pair in wordFrequency)
                {
                    answerChance[pair.Key] = Sigmoid(pair.Value);
                }
            }

            if (!readFrequenciesFromFile)
            {
                using var jsonWritingScope = new ScopeLogger("Writing JSON");
                Logger.Instance.LogLine($"word frequencies: {answerChance.Count()}");
                var serializer = JsonSerializer.Create();
                using var writer = new StringWriter();
                using var jsonWriter = new JsonTextWriter(writer);
                serializer.Serialize(jsonWriter, answerChance);
                string fileContents = writer.ToString();
                Logger.Instance.LogLine($"file size: {fileContents.Length}");
                File.WriteAllText("wordle-frequencies.json", fileContents);
            }

            return answerChance;
        }

        private static async Task<List<Guess>> GenerateAllGuesses(IEnumerable<string> words, HashSet<string> answers)
        {
            using var fullScope = new ScopeLogger("GenerateAllGuesses - overall");
            if (!Directory.Exists("wordle-map"))
            {
                Directory.CreateDirectory("wordle-map");
            }

            int expectedTotalCount = answers.Count();
            ConcurrentDictionary<Guess, int> guesses = new ConcurrentDictionary<Guess, int>();
            List<Task> tasks = new List<Task>();
            using (var logScope = new ScopeLogger("GenerateAllGuesses - creating tasks"))
            {
                foreach (var word in words)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        bool readGuessFromFile = TryReadGuess(expectedTotalCount, word, out Guess guess, out string filePath);

                        if (guess == null)
                        {
                            readGuessFromFile = false;
                            guess = new Guess(word, answers.Contains(word));
                            await guess.InitializeAsync(answers);
                        }

                        if (!readGuessFromFile)
                        {
                            var serializer = JsonSerializer.Create();
                            using var writer = new StringWriter();
                            using var jsonWriter = new JsonTextWriter(writer);
                            serializer.Serialize(jsonWriter, guess);
                            File.WriteAllText(filePath, writer.ToString());
                        }

                        Debug.Assert(guesses.TryAdd(guess, 0));
                    }));
                }
            }

            using (var logScope = new ScopeLogger($"GenerateAllGuesses - waiting for tasks ({tasks.Count}) to complete"))
            {
                await Task.WhenAll(tasks);
            }

            return guesses.Keys.OrderByDescending(x => x.GetEntropy()).ToList();
        }


        private static bool TryReadGuess(int totalAnswerCount, string word, out Guess guess, out string filePath)
        {
            guess = null;
            filePath = Path.Combine("wordle-map", $"{word}.json");
            if (File.Exists(filePath))
            {
                using var jsonReader = new JsonTextReader(new StringReader(File.ReadAllText(filePath)));
                var serializer = JsonSerializer.Create();
                try
                {
                    Guess tempGuess = serializer.Deserialize<Guess>(jsonReader);
                    if (tempGuess.TotalAnswerCount == totalAnswerCount)
                    {
                        guess = tempGuess;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogLine($"File '{filePath}' Error deserializing JSON: {ex.Message}", LogLevel.Error);
                }
            }

            return false;
        }

        private static void WriteGuesses(IEnumerable<Guess> guesses, bool forceShowAll = false)
        {
            int counter = 1;
            ConsoleColor originalColor = Console.ForegroundColor;
            foreach (var guess in guesses)
            {
                if (guess.IsAnswer || forceShowAll)
                {
                    Console.WriteLine($"{counter}\t{guess.Word} - {guess.GetEntropy():0.0000} - keys: {guess.Groups.Count}\t - values: {guess.Groups.Values.Sum(x => x.Words.Count)}");
                }

                counter++;
            }
            Console.ForegroundColor = originalColor;
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

        public static bool ContainsAny(string str, char[] chars)
        {
            foreach (char c in chars)
            {
                if (str.Contains(c))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
