using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wordle_Parallel
{
    public class Group
    {
        public Group(string word, string comparison)
        {
            this.GivenWord = word;
            this.Comparison = comparison;
            this.Words = new HashSet<string>();
        }

        [JsonProperty(Required = Required.Always)]
        public string GivenWord { get; }

        [JsonProperty(Required = Required.Always)]
        public string Comparison { get; }

        [JsonProperty(Required = Required.Always)]
        public HashSet<string> Words { get; }

        public double GetProbability(int totalAnswers)
        {
            return (double)this.Words.Count / totalAnswers;
        }

        public double GetBits(int totalAnswers)
        {
            return -1.0 * Math.Log(this.GetProbability(totalAnswers), 2);
        }

        public double GetEntropy(int totalAnswers)
        {
            return this.GetProbability(totalAnswers) * this.GetBits(totalAnswers);
        }
    }
}
