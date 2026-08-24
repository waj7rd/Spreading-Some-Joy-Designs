namespace SpreadingJoy.Tests;

// Collection needs nothing. Postage needs an address, and only if the studio
// offers postage at all. These lock down which of those is which, because both
// the public request path and the staff order path lean on the same function.
public class FulfilmentTests
{
    private static readonly ShippingAddress Complete =
        new("14 Sycamore Street", null, "Sedalia", "MO", "65301");

    // ---- what counts as shipping ----

    [Fact]
    public void An_unrecognised_method_reads_as_collection_rather_than_shipping()
    {
        // The safe direction. A collection order misread as shipped goes
        // nowhere and gets noticed; the reverse posts a parcel nobody charged
        // for and nobody expected.
        Assert.Equal(FulfilmentMethod.Pickup, FulfilmentMethod.Normalise("Teleport"));
        Assert.Equal(FulfilmentMethod.Pickup, FulfilmentMethod.Normalise(null));
        Assert.Equal(FulfilmentMethod.Pickup, FulfilmentMethod.Normalise(""));
    }

    [Fact]
    public void The_method_is_read_case_insensitively_because_it_arrives_from_a_form()
    {
        Assert.Equal(FulfilmentMethod.Shipping, FulfilmentMethod.Normalise("shipping"));
        Assert.Equal(FulfilmentMethod.Shipping, FulfilmentMethod.Normalise("SHIPPING"));
        Assert.True(FulfilmentMethod.IsShipping("Shipping"));
    }

    // ---- collection ----

    [Fact]
    public void A_collection_order_needs_no_address()
    {
        Assert.Null(Fulfilment.Check(FulfilmentMethod.Pickup, ShippingAddress.None, studioOffersShipping: true));
    }

    [Fact]
    public void A_collection_order_is_accepted_even_when_the_studio_does_not_ship()
    {
        // Switching shipping off must not stop the studio taking orders.
        Assert.Null(Fulfilment.Check(FulfilmentMethod.Pickup, ShippingAddress.None, studioOffersShipping: false));
    }

    [Fact]
    public void A_collection_order_keeps_no_address_even_when_one_was_supplied()
    {
        // A half-filled address sitting on a collection order is the kind of
        // thing that later gets read as a shipping label.
        var stored = Fulfilment.ToStore(FulfilmentMethod.Pickup, Complete);

        Assert.True(stored.IsEmpty);
    }

    // ---- shipping ----

    [Fact]
    public void A_complete_shipping_address_is_accepted()
    {
        Assert.Null(Fulfilment.Check(FulfilmentMethod.Shipping, Complete, studioOffersShipping: true));
    }

    [Fact]
    public void Shipping_is_refused_when_the_studio_does_not_offer_it()
    {
        // The form only renders the choice when shipping is on, so reaching this
        // means the post was built by hand or the setting changed underneath a
        // page somebody had open.
        var message = Fulfilment.Check(FulfilmentMethod.Shipping, Complete, studioOffersShipping: false);

        Assert.NotNull(message);
        Assert.Contains("not shipping", message);
    }

    [Theory]
    [InlineData(null, "Sedalia", "MO", "65301", "street address")]
    [InlineData("14 Sycamore Street", null, "MO", "65301", "city")]
    [InlineData("14 Sycamore Street", "Sedalia", null, "65301", "state")]
    [InlineData("14 Sycamore Street", "Sedalia", "MO", null, "ZIP")]
    public void Every_part_of_the_address_except_the_second_line_is_required(
        string? line1, string? city, string? state, string? postalCode, string expected)
    {
        var message = Fulfilment.Check(
            FulfilmentMethod.Shipping,
            new ShippingAddress(line1, null, city, state, postalCode),
            studioOffersShipping: true);

        Assert.NotNull(message);

        // The refusal has to name the field that's missing, or the customer is
        // left comparing their address against a generic complaint.
        Assert.Contains(expected, message);
    }

    [Fact]
    public void The_second_address_line_is_optional()
    {
        Assert.Null(Fulfilment.Check(
            FulfilmentMethod.Shipping,
            new ShippingAddress("14 Sycamore Street", null, "Sedalia", "MO", "65301"),
            studioOffersShipping: true));
    }

    [Fact]
    public void Whitespace_is_not_an_address()
    {
        // "   " passes a null check and fails a delivery.
        var message = Fulfilment.Check(
            FulfilmentMethod.Shipping,
            new ShippingAddress("   ", null, "Sedalia", "MO", "65301"),
            studioOffersShipping: true);

        Assert.NotNull(message);
    }

    [Fact]
    public void An_over_long_field_is_refused_rather_than_left_to_the_database()
    {
        // Caught here so the customer reads a sentence, rather than the request
        // failing on a truncation error somewhere behind the form.
        var message = Fulfilment.Check(
            FulfilmentMethod.Shipping,
            Complete with { Line1 = new string('x', ShippingAddress.MaxLineLength + 1) },
            studioOffersShipping: true);

        Assert.NotNull(message);
        Assert.Contains(ShippingAddress.MaxLineLength.ToString(), message);
    }

    [Fact]
    public void A_stored_shipping_address_is_trimmed()
    {
        var stored = Fulfilment.ToStore(
            FulfilmentMethod.Shipping,
            new ShippingAddress("  14 Sycamore Street  ", "  ", " Sedalia ", " MO ", " 65301 "));

        Assert.Equal("14 Sycamore Street", stored.Line1);
        Assert.Equal("Sedalia", stored.City);
        Assert.Equal("MO", stored.State);
        Assert.Equal("65301", stored.PostalCode);

        // An empty second line is stored as absent, not as a blank string, so
        // the address renders as three lines rather than four.
        Assert.Null(stored.Line2);
    }
}
