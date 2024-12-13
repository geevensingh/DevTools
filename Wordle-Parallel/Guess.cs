using Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wordle_Parallel
{
    public class Guess
    {
        private Task initializationTask = null;

        [JsonProperty(Required = Required.Always)]
        public string Word { get; }

        [JsonProperty(Required = Required.Always)]
        public bool IsAnswer { get; }

        [JsonProperty(Required = Required.Always)]
        public Dictionary<string, Group> Groups { get; private set; }

        [JsonProperty(Required = Required.Always)]
        public int? TotalAnswerCount { get; private set; } = null;

        public Guess(string word, bool isAnswer)
        {
            this.Word = word;
            this.IsAnswer = isAnswer;
            this.Groups = null;
        }

        public double GetBits()
        {
            return this.EnsureInitialized(() => this.Groups.Values.Sum(group => group.GetBits(this.TotalAnswerCount.Value)));
        }

        public double GetEntropy()
        {
            return this.EnsureInitialized(() => this.Groups.Values.Sum(group => group.GetEntropy(this.TotalAnswerCount.Value)));
        }

        public Task InitializeAsync(IEnumerable<string> answers)
        {
            if (this.initializationTask != null)
            {
                return this.initializationTask;
            }

            this.initializationTask = Task.Run(() =>
            {
                this.Initialize(answers);
            });

            return this.initializationTask;
        }

        private void Initialize(IEnumerable<string> answers)
        {
            this.TotalAnswerCount = answers.Count();
            var groups = new Dictionary<string, Group>();
            foreach (string answer in answers)
            {
                string comparison = Program.GetComparison(answer, this.Word);
                if (!groups.ContainsKey(comparison))
                {
                    groups.Add(comparison, new Group(this.Word, comparison));
                }

                groups[comparison].Words.Add(answer);
            }

            this.Groups = groups;
        }

        private T EnsureInitialized<T>(Func<T> func)
        {
            Debug.Assert(this.initializationTask == null || this.initializationTask.IsCompleted);
            Debug.Assert(this.TotalAnswerCount != null);
            Debug.Assert(this.Groups != null);
            return func();
        }
    }
}
