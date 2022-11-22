using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WeaponEvaluator
{
    internal class WeaponList
    {
        private IEnumerable<Weapon> weapons;
        private Dictionary<long, IEnumerable<Weapon>> weaponsById = new Dictionary<long, IEnumerable<Weapon>>();

        public WeaponList(IEnumerable<string> rawList)
        {
            this.weapons = rawList.Select(x => Weapon.FromWishList(x));
            foreach (var group in this.weapons.GroupBy(x => x.WeaponId))
            {
                this.weaponsById[group.Key] = group;
            }
        }

        public int? GetPercentage(DIMWeapon dimWeapon)
        {
            var dimWeaponPerks = dimWeapon.Perks.ToArray();
            if (!this.weaponsById.ContainsKey(dimWeapon.HashAsLong))
            {
                return null;
            }

            int bestPercentage = 0;
            var matches = this.weaponsById[dimWeapon.HashAsLong];
            foreach (var match in matches)
            {
                bestPercentage = Math.Max(bestPercentage, match.GetPercentage(dimWeaponPerks));
            }
            return bestPercentage;
        }

        internal List<Weapon> GetByWeaponId(long id)
        {
            return this.weaponsById[id].ToList();
        }
    }
}
