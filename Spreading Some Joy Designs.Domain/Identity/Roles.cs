namespace SpreadingJoy.Domain.Identity;

// The staff role vocabulary. These strings go into the Role column and into the
// role claim, so they have to agree with Users.Role in the database.
public static class Roles
{
    // Runs the studio. Everything, including staff accounts.
    public const string Admin = "Admin";

    // Everything except managing staff accounts. Approves artwork, handles
    // order requests, changes the catalogue.
    public const string Manager = "Manager";

    // Works the press: sees the production board, moves orders through their
    // statuses. Can't approve artwork — deciding whether an image is safe to
    // print is a judgement call the studio answers for.
    public const string Associate = "Associate";

    public static readonly string[] All = [Admin, Manager, Associate];
}

// Named authorization policies, referenced from [Authorize(Policy = ...)].
// Policies rather than bare [Authorize(Roles = "...")] so the rule lives in one
// place and reads as an intent ("who may approve artwork") rather than a list of
// role names scattered across controllers.
public static class Policies
{
    // Add, edit, deactivate staff accounts. Admin only.
    public const string ManageStaff = "ManageStaff";

    // Create, edit, or archive customer records.
    public const string ManageCustomers = "ManageCustomers";

    // View customer records without changing them.
    public const string ViewCustomers = "ViewCustomers";

    // Move orders through their statuses; work the production board.
    public const string ManageOrders = "ManageOrders";

    // Change the product catalogue: garments, prices, print areas.
    public const string ManageCatalog = "ManageCatalog";

    // Approve or reject submitted artwork. Deliberately narrower than
    // ManageOrders — an associate can print a job without being the person who
    // decides that a picture is legal to print.
    public const string ModerateArtwork = "ModerateArtwork";

    // Change how the studio runs: hours, capacity, turnaround, contact details.
    // Not the same as ManageStaff — this is operational, not administrative,
    // and a manager should be able to change Saturday hours.
    public const string ManageStudioSettings = "ManageStudioSettings";
}
