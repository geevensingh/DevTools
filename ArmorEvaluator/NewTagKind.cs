namespace ArmorEvaluator
{
    public enum NewTagKind
    {
        AbsoluteKeep,
        Keep,
        Junk,
        Favorite,
        Infuse,
        None,
    }

    public static class NewTagKindExtensions
    {
        public static string ToOldTagString(this NewTagKind kind)
        {
            switch (kind)
            {
                case NewTagKind.AbsoluteKeep:
                case NewTagKind.Keep:
                    return "keep";
                case NewTagKind.Junk:
                    return "junk";
                case NewTagKind.Favorite:
                    return "keep";
                case NewTagKind.Infuse:
                    return "infuse";
                case NewTagKind.None:
                    return "";
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), $"Unexpected value: '{kind}'");
            }
        }

        public static NewTagKind ToNewTag(this string value)
        {
            switch (value)
            {
                case "keep":
                    return NewTagKind.Keep;
                case "junk":
                    return NewTagKind.Junk;
                case "favorite":
                    return NewTagKind.Favorite;
                case "infuse":
                    return NewTagKind.Infuse;
                case "":
                    return NewTagKind.None;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), $"Unexpected value: '{value}'");
            }
        }
    }
}
