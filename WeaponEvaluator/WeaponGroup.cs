using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace WeaponEvaluator
{
    internal class WeaponGroup
    {
        public WeaponGroup(string type, string slot, string element, string frameStyle, IEnumerable<DIMWeapon> weapons)
        {
            this.Type = type;
            this.Slot = slot;
            this.Element = element;
            this.FrameStyle = frameStyle;
            this.Weapons = weapons;

            MaxTypeLength = Math.Max(MaxTypeLength, this.Type.Length + 1);
            MaxSlotLength = Math.Max(MaxSlotLength, this.Slot.Length + 1);
            MaxElementLength = Math.Max(MaxElementLength, this.Element.Length + 1);
            MaxFrameStyleLength = Math.Max(MaxFrameStyleLength, this.FrameStyle.Length + 1);
            MaxNameLength = Math.Max(MaxNameLength, this.Weapons.Max(x => x.Name.Length + 1));
        }

        public string Type { get; }
        public string Slot { get; }
        public string Element { get; }
        public string FrameStyle { get; }
        public IEnumerable<DIMWeapon> Weapons { get; }
        public int Count => this.Weapons.Count();


        public string DIMQuery
        {
            get
            {
                if (this.Count == 1)
                {
                    return $"id:{this.Weapons.First().Id}";
                }

                return $"{this.GetTypeForDIMQuery()} is:{this.Slot} is:{this.Element} {this.GetFrameStyleForDIMQuery()}";
            }
        }

        private string GetTypeForDIMQuery()
        {
            string type = this.Type.Replace(" ", null);
            if (type.ToLower() == "submachinegun")
            {
                type = "Submachine";
            }
            return $"is:{type}";
        }

        private string GetFrameStyleForDIMQuery()
        {
            string frameStyleQuery = this.FrameStyle;
            if (frameStyleQuery.Contains(" "))
            {
                frameStyleQuery = '\"' + frameStyleQuery + '\"';
            }

            return $"perkname:{frameStyleQuery}";

        }

        public static int MaxTypeLength { get; private set; }
        public static int MaxSlotLength { get; private set; }
        public static int MaxElementLength { get; private set; }
        public static int MaxFrameStyleLength { get; private set; }
        public static int MaxNameLength { get; private set; }

        public static IEnumerable<WeaponGroup> CreateGroups(IEnumerable<DIMWeapon> allWeapons)
        {
            var weapons = allWeapons.Where(x => x.Tier != "Exotic").Where(x => x.GetNewTag() != "junk" && x.GetNewTag() != "influse" && (!x.Crafted || x.CraftedLevel >= 10));
            List<WeaponGroup> groups = new List<WeaponGroup>();
            foreach (var typeGroup in weapons.GroupBy(x => x.Type).OrderBy(x => x.Key))
            {
                foreach (var slotGroup in typeGroup.GroupBy(x => x.Category).OrderBy(x => GetSlotIndex(x.Key)))
                {
                    foreach (var frameStyleGroup in slotGroup.GroupBy(x => x.FrameStyle).OrderBy(x => x.Key))
                    {
                        foreach (var elementGroup in frameStyleGroup.GroupBy(x => x.Element).OrderBy(x => x.Key))
                        {
                            groups.Add(new WeaponGroup(typeGroup.Key, slotGroup.Key, elementGroup.Key, frameStyleGroup.Key, elementGroup));
                        }
                    }
                }
            }

            return groups;
        }

        private static int GetSlotIndex(string slot)
        {
            switch (slot)
            {
                case "KineticSlot":
                    return 0;
                case "Energy":
                    return 1;
                case "Power":
                    return 2;
                default:
                    throw new ArgumentException($"Unknown slot {slot}", nameof(slot));
            }
        }
    }
}
