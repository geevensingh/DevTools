namespace SpellingBee
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    internal class Program
    {
        static async Task Main(string[] args)
        {


            HashSet<string> words = new HashSet<string>(File.ReadAllLines("words_alpha.txt").Where(x => x.Length > 3).Select(x => x.ToLower()));

            char center = 'n';
            char[] others = "phaety".ToCharArray();
            char[] all = others.Append(center).ToArray();

            var results = words
                .Where(x => x.Contains(center))
                .Where(x => x.ToCharArray().All(y => all.Contains(y)))
                .OrderBy(x => x.Length)
                .ThenBy(x => x);
            foreach (var word in results) {
                Console.WriteLine(word);
            }

            results = results.Where(x => x.ToCharArray().Distinct().Count() == all.Length).OrderBy(x => x.Length).ThenBy(x => x);
            Console.WriteLine("\r\nPangram:");
            foreach (var word in results)
            {
                Console.WriteLine(word);
            }
        }
    }
}
