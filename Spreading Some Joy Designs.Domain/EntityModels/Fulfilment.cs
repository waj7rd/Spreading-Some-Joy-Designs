namespace SpreadingJoy.Domain.EntityModels;

// How a finished order reaches the customer.
//
// String constants rather than an enum, for the same reason as OrderStatus:
// these land in an NVARCHAR column and get read directly in SQL.
public static class FulfilmentMethod
{
    public const string Pickup = "Pickup";
    public const string Shipping = "Shipping";

    public static readonly string[] All = [Pickup, Shipping];

    // Case-insensitive because this arrives from a form post. Anything that
    // isn't recognisably shipping is pickup — the safe default, since a pickup
    // order misread as shipped would go nowhere, while the reverse just means
    // somebody rings the customer.
    public static bool IsShipping(string? method) =>
        string.Equals(method, Shipping, StringComparison.OrdinalIgnoreCase);

    // Normalises a posted value onto one of the two constants. An unknown
    // string becomes Pickup rather than being stored as typed, so the column
    // only ever holds a value the rest of the code knows how to read.
    public static string Normalise(string? method) =>
        IsShipping(method) ? Shipping : Pickup;
}

// Where a shipped order is going.
//
// A record rather than five loose parameters threaded through two logic layers:
// the address travels together or not at all, and there's one place that
// decides whether it's usable rather than one per caller.
public record ShippingAddress(
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? PostalCode)
{
    public static readonly ShippingAddress None = new(null, null, null, null, null);

    // Field lengths. Stated here rather than only in the schema because the
    // refusal a customer reads should come from the rule, not from a truncation
    // error on the way to the database.
    public const int MaxLineLength = 200;
    public const int MaxCityLength = 100;
    public const int MaxStateLength = 50;
    public const int MaxPostalCodeLength = 20;

    public ShippingAddress Trimmed() => new(
        Blank(Line1), Blank(Line2), Blank(City), Blank(State), Blank(PostalCode));

    public bool IsEmpty =>
        Blank(Line1) == null && Blank(Line2) == null && Blank(City) == null &&
        Blank(State) == null && Blank(PostalCode) == null;

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

// The rules about fulfilment, in one place because both the public request path
// and the staff order path arrive at them and neither may skip a check.
public static class Fulfilment
{
    // Null when the choice is usable, otherwise the reason it isn't.
    //
    // Deliberately not a check that the address exists — it's a check that the
    // address exists *if it's needed*. A pickup order with no address is
    // correct, not incomplete.
    public static string? Check(string method, ShippingAddress address, bool studioOffersShipping)
    {
        if (!FulfilmentMethod.IsShipping(method))
            return null;

        if (!studioOffersShipping)
            return "We're not shipping at the moment — pick collection from the studio instead.";

        var trimmed = address.Trimmed();

        if (trimmed.Line1 == null)
            return "We need a street address to ship to.";

        if (trimmed.City == null)
            return "We need a city to ship to.";

        if (trimmed.State == null)
            return "We need a state to ship to.";

        if (trimmed.PostalCode == null)
            return "We need a ZIP code to ship to.";

        if (trimmed.Line1.Length > ShippingAddress.MaxLineLength ||
            (trimmed.Line2?.Length ?? 0) > ShippingAddress.MaxLineLength)
            return $"Each address line has to be {ShippingAddress.MaxLineLength} characters or fewer.";

        if (trimmed.City.Length > ShippingAddress.MaxCityLength)
            return $"The city has to be {ShippingAddress.MaxCityLength} characters or fewer.";

        if (trimmed.State.Length > ShippingAddress.MaxStateLength)
            return $"The state has to be {ShippingAddress.MaxStateLength} characters or fewer.";

        if (trimmed.PostalCode.Length > ShippingAddress.MaxPostalCodeLength)
            return $"The ZIP code has to be {ShippingAddress.MaxPostalCodeLength} characters or fewer.";

        return null;
    }

    // What to actually store. A pickup order keeps no address at all, even if
    // one was posted — a half-filled address sitting on a collection order is
    // the kind of thing that later gets read as a shipping label.
    public static ShippingAddress ToStore(string method, ShippingAddress address) =>
        FulfilmentMethod.IsShipping(method) ? address.Trimmed() : ShippingAddress.None;
}
