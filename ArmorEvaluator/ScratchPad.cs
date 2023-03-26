using System.Diagnostics;

namespace ArmorEvaluator
{
    internal record ScratchPad : IEquatable<ScratchPad>
    {
        private static HashSet<string> ScratchPads = new HashSet<string>();

        public ScratchPad(Item item, HashSet<AppliedWeightSet> appliedWeightSets)
        {
            Debug.Assert(!ScratchPads.Contains(item.Id));
            ScratchPads.Add(item.Id);

            Item = item;
            Weights = appliedWeightSets;
            NewTag = item.Tag.ToNewTag();
        }

        public Item Item { get; set; }
        public HashSet<AppliedWeightSet> Weights { get; set; }

        public bool MeetsThreshold => Weights.Any(x => x.MeetsThreshold);
        public string WeightsThatMeetThreshold => string.Join(",", Weights.Where(x => x.MeetsThreshold).Select(x => x.WeightSet.Name));

        public int SpecialLevel => Item.SpecialLevel;

        public float AbsoluteValue => Weights.Sum(x => (x.Sum - x.WeightSet.Threshold.GetApplicableThreshold(Item)) / x.WeightSet.Threshold.GetApplicableThreshold(Item));

        public NewTagKind NewTag { get; private set; }

        public string NewTagReason { get; private set; }

        public bool TagChanged => NewTag.ToOldTagString() != Item.Tag;

        public void SetTag(NewTagKind newTag, string reason)
        {
            this.NewTag = newTag;
            this.NewTagReason = $"{newTag} -> {reason}";
        }

        public bool IsJunk => NewTag == NewTagKind.Junk;
        public bool CanChangeTag => NewTag != NewTagKind.Favorite && NewTag != NewTagKind.AbsoluteKeep;

        //public bool Equals(ScratchPad? other)
        //{
        //    if (ReferenceEquals(null, other)) return false;
        //    if (ReferenceEquals(this, other)) return true;
        //    return this.Item.Equals(other.Item);
        //}
    }
}
