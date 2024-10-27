using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wordle_Parallel
{
    internal class Group
    {
        private int total;

        public Group(string givenWord, string comparison, int total)
        {
            this.total = total;

            this.GivenWord = givenWord;
            this.Comparison = comparison;
            this.Words = new HashSet<string>();
        }

        public string GivenWord { get; }

        public string Comparison { get; }

        public HashSet<string> Words { get; }

        public double Probability => (double)this.Words.Count / this.total;
        public double Bits => -1.0 * Math.Log(this.Probability, 2);
        public double Entropy => this.Probability * this.Bits;
    }
}
