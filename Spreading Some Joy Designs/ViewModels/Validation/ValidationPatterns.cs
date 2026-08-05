namespace SpreadingJoy.ViewModels.Validation;

// Shared regular expressions and messages, so the same field validates the same
// way on every form it appears on.
public static class ValidationPatterns
{
    // Deliberately loose. Phone numbers arrive with spaces, dashes, brackets and
    // country codes, and a strict pattern rejects real people far more often
    // than it catches bad data. The studio rings the number; it doesn't parse it.
    public const string Phone = @"^[0-9 ()+\-\.]{7,30}$";
    public const string PhoneMessage = "That doesn't look like a phone number.";

    public const string HexColour = "^#[0-9a-fA-F]{6}$";
    public const string HexColourMessage = "Use a hex colour like #1a1a1a.";
}
