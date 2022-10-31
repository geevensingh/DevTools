using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WeaponEvaluator
{
    internal class Perk
    {
        private static PerkLookupCache cache = new PerkLookupCache();
        public Perk(string name, long id)
        {
            this.Name = name;
            this.Id = id;
        }

        public string Name { get; set; }
        public long Id { get; set; }

        public static async Task<Perk> CreateById(long id)
        {
            return new Perk(await Perk.cache.GetPerkName(id), id);
        }
    }
}
