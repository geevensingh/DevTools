using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wordle_Parallel
{
    internal class Guess
    {
        public string Word { get; }
        public Dictionary<string, Group> Groups { get; }

        public Guess(string word)
        {
            this.Word = word;
            this.Groups = new Dictionary<string, Group>();
        }

        public double Bits => this.Groups.Values.Sum(group => group.Bits);
        public double Entropy => this.Groups.Values.Sum(group => group.Entropy);

        public Task InitializeAsync(IEnumerable<string> answers)
        {
            return Task.Run(() =>
            {
                this.Initialize(answers);
            });
        }

        public void Initialize(IEnumerable<string> answers)
        {
            int total = answers.Count();
            foreach (string answer in answers)
            {
                string comparison = Program.GetComparison(this.Word, answer);
                if (!this.Groups.ContainsKey(comparison))
                {
                    this.Groups.Add(comparison, new Group(this.Word, comparison, total));
                }

                this.Groups[comparison].Words.Add(answer);
            }
        }
    }
}
