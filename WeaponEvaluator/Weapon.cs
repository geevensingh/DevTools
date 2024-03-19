using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WeaponEvaluator
{
    internal class Weapon
    {
        public Weapon(long weaponId, long[] perkIds)
        {
            WeaponId = weaponId;
            PerkIds = perkIds;
            PerkNames = perkIds.Select(x => Perk.CreateById(x).Result.Name);
        }

        public long WeaponId { get; }
        public long[] PerkIds { get; }
        public IEnumerable<string> PerkNames { get; }

        internal static Weapon FromWishList(string wishListLine)
        {
            wishListLine = wishListLine.Trim().TrimStart("dimwishlist:item=").TrimStart("-");
            string[] parts = wishListLine.Split("&perks=");
            Debug.Assert(parts.Length == 1 || parts.Length == 2);
            Debug.Assert(long.TryParse(parts[0], out long _));
            long weaponId = long.Parse(parts[0]);

            long[] perkIds = new long[] { };
            if (parts.Length > 1)
            {
                perkIds = parts[1].Split(",").Where(x => !string.IsNullOrEmpty(x)).Select(x => long.Parse(x.Trim())).ToArray();
            }

            return new Weapon(weaponId, perkIds);
        }

        internal int GetPercentage(IEnumerable<string> dimWeaponPerks)
        {
            if (this.PerkNames.Count() == 0)
            {
                return 100;
            }

            int foundCount = 0;
            foreach (string perkName in this.PerkNames)
            {
                if (dimWeaponPerks.Contains(perkName))
                {
                    foundCount++;
                }
            }
            return foundCount * 100 / this.PerkNames.Count();
        }
    }
}
