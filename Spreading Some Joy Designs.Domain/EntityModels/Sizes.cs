namespace SpreadingJoy.Domain.EntityModels;

// The size vocabulary. Products choose a subset of these; anything outside the
// list is rejected rather than silently accepted, because a size nobody stocks
// becomes a phone call on the day of collection.
public static class Sizes
{
    public const string Small = "S";
    public const string Medium = "M";
    public const string Large = "L";
    public const string ExtraLarge = "XL";
    public const string TwoExtraLarge = "2XL";
    public const string ThreeExtraLarge = "3XL";
    public const string FourExtraLarge = "4XL";

    public static readonly string[] All =
        [Small, Medium, Large, ExtraLarge, TwoExtraLarge, ThreeExtraLarge, FourExtraLarge];

    // Sizes that cost the studio more to buy, and so carry the product's
    // extended-size upcharge. Which sizes those are is a fact about garment
    // wholesalers, not about any one product, so it lives here.
    private static readonly HashSet<string> Extended =
        new(StringComparer.OrdinalIgnoreCase) { TwoExtraLarge, ThreeExtraLarge, FourExtraLarge };

    public static bool IsExtended(string sizeCode) => Extended.Contains(sizeCode);

    public static bool IsKnown(string sizeCode) =>
        All.Contains(sizeCode, StringComparer.OrdinalIgnoreCase);

    // Sorts sizes into the order a human expects to see them, rather than
    // alphabetically — which would put 2XL before L and S after M.
    public static int SortKey(string sizeCode)
    {
        var index = Array.FindIndex(All, s => string.Equals(s, sizeCode, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? int.MaxValue : index;
    }
}
