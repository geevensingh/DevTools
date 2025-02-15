using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Strands
{
    internal class Dictionary
    {
        public string Prefix { get; private set; }

        public char Letter { get; private set; }
        public bool IsValid { get; }
        public IEnumerable<string> Remainders { get; private set; }
        public string Word => Prefix + Letter;

        public Dictionary(string prefix, char letter, IEnumerable<string> remainders)
        {
            Prefix = prefix;
            Letter = letter;
            IsValid = remainders.Any(x => x.Length == 0);
            Remainders = remainders.Where(x => x.Length != 0);
        }

        public Dictionary GetNext(char letter)
        {
            var enumerable = Remainders.Where(x => x[0] == letter).ToArray();
            var remainders = enumerable.Select(x => x.Substring(1)).ToArray();
            return new Dictionary(Prefix + Letter, letter, remainders);
        }

        public static IEnumerable<Dictionary> Create(IEnumerable<string> words)
        {
            var grouped = words.GroupBy(x => x[0]);
            return grouped.Select(x => new Dictionary(string.Empty, x.Key, x.Select(y => y.Substring(1))));
        }
    }
}
