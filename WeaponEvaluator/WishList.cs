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

        public int GetPercentage(DIMWeapon dimWeapon)
        {
            int bestPercentage = 0;
            var dimWeaponPerks = dimWeapon.Perks.ToArray();
            if (!this.weaponsById.ContainsKey(dimWeapon.HashAsLong))
            {
                return 0;
            }
            
            var matches = this.weaponsById[dimWeapon.HashAsLong];
            foreach (var match in matches)
            {
                bestPercentage = Math.Max(bestPercentage, match.GetPercentage(dimWeaponPerks));
            }
            return bestPercentage;
        }

        public static async Task<Tuple<WeaponList, WeaponList>> CreateLists()
        {
            string wishListRawContent;
            using (var client = new HttpClient())
            {
                wishListRawContent = await client.GetStringAsync(@"https://raw.githubusercontent.com/48klocs/dim-wish-list-sources/master/choosy_voltron.txt");
            }
            await File.WriteAllTextAsync(@"D:\Repos\DevTools\wishlist.txt", wishListRawContent);

            IEnumerable<string> wishListRawLines = wishListRawContent.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            wishListRawLines = wishListRawLines.Select(x =>
            {
                int index = x.IndexOf("#");
                if (index > 0)
                {
                    x = x.Substring(0, index);
                }
                return x;
            });
            IEnumerable<string> wishListRaw = wishListRawLines.Where(x => x.StartsWith("dimwishlist:item=") && !x.StartsWith("dimwishlist:item=-"));
            IEnumerable<string> trashListRaw = wishListRawLines.Where(x => x.StartsWith("dimwishlist:item=-"));

            return new Tuple<WeaponList, WeaponList>(new WeaponList(wishListRaw), new WeaponList(trashListRaw));
        }

    }
}
