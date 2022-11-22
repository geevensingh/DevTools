using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WeaponEvaluator
{
    internal class WishList
    {
        private WeaponList wishList;
        private WeaponList trashList;

        public WishList(IEnumerable<string> wishListRaw, IEnumerable<string> trashListRaw)
        {
            this.wishList = new WeaponList(wishListRaw);
            this.trashList = new WeaponList(trashListRaw);
        }

        public int? GetPercentage(DIMWeapon dimWeapon)
        {
            int? wishPercentage = this.wishList.GetPercentage(dimWeapon);
            int? trashPercentage = this.trashList.GetPercentage(dimWeapon);
            if (trashPercentage == null)
            {
                return wishPercentage;
            }

            if (wishPercentage == null)
            {
                return trashPercentage.Value * -1;
            }

            return wishPercentage.Value - trashPercentage.Value;
        }

        public static async Task<WishList> CreateLists(string? filePath = null)
        {
            string wishListRawContent;
            using (var client = new HttpClient())
            {
                //const string wishListUrl = @"https://raw.githubusercontent.com/48klocs/dim-wish-list-sources/master/choosy_voltron.txt";
                const string wishListUrl = @"https://raw.githubusercontent.com/48klocs/dim-wish-list-sources/master/voltron.txt";
                wishListRawContent = await client.GetStringAsync(wishListUrl);
            }
            if (!string.IsNullOrEmpty(filePath))
            {
                await File.WriteAllTextAsync(filePath, wishListRawContent);
            }

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

            return new WishList(wishListRaw, trashListRaw);
        }

    }
}
