using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Utilities;

namespace WeaponEvaluator
{
    internal class PerkLookupCache
    {
        private const string unknownPerkName = "Unknown";
        private Dictionary<long, string> cache = new Dictionary<long, string>();
        private string filePath;

        public PerkLookupCache()
            : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PerkLookupCache.txt"))
        {
        }

        public PerkLookupCache(string filePath)
        {
            this.filePath = filePath;
            this.ReadFile();
        }

        public async Task<string> GetPerkName(long perkId)
        {
            if (this.cache.ContainsKey(perkId))
            {
                return this.cache[perkId];
            }

            string responseContent;
            using (var client = new HttpClient())
            {
                HttpResponseMessage responseMessage = await client.GetAsync($"https://www.light.gg/db/items/{perkId}/");
                if (!responseMessage.IsSuccessStatusCode)
                {
                    Console.WriteLine($"{perkId}\t - unable to determine ({responseMessage.StatusCode})");
                    this.cache[perkId] = unknownPerkName;
                    return this.cache[perkId];
                }

                responseContent = await responseMessage.Content.ReadAsStringAsync();
            }

            responseContent = responseContent.TrimUpToAndIncluding("<title>");
            responseContent = responseContent.TrimAtAndAfter("</title>");
            string[] parts = responseContent.Split(" - ").Select(x => x.Trim()).ToArray();
            Debug.Assert(parts.Length == 3);
            Debug.Assert(parts[2] == "light.gg");

            this.cache[perkId] = HttpUtility.HtmlDecode(parts[0]);
            this.WriteFile();
            return this.cache[perkId];
        }

        private void ReadFile()
        {
            if (!File.Exists(this.filePath))
            {
                Console.WriteLine($"Cannot find perk lookup cache file: {this.filePath}");
                return;
            }

            foreach (string[] x in File.ReadAllLines(this.filePath).Select(line => line.Split("//")))
            {
                Debug.Assert(x.Length == 2);
                long id = long.Parse(x[0]);
                Debug.Assert(!this.cache.ContainsKey(id));
                this.cache[id] = x[1];
            }
        }

        private void WriteFile()
        {
            List<string> lines = new List<string>();
            foreach (var pair in this.cache)
            {
                if (pair.Value != unknownPerkName)
                {
                    lines.Add($"{pair.Key}//{pair.Value}");
                }
            }
            File.WriteAllLines(this.filePath, lines);
        }
    }
}
