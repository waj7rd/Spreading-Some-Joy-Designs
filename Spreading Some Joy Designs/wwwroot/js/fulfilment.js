// Shows the shipping address block, and the postage line beside the preview,
// when the customer asks for the order to be posted.
//
// Nothing here is a source of truth. The fields exist in the DOM whether or not
// they're visible, so a refused submission comes back with what was typed still
// in them, and the server decides what an order actually needs: the required
// fields are checked in OrdersController, and Fulfilment.Check in the Domain is
// the rule that gates the order regardless of what this file does.
//
// With scripting off, whichever state the server rendered stays put and the form
// still submits correctly — the address fields are simply always visible once
// postage has been chosen and the page re-rendered.
//
// Two forms use this now, the garment order form and the gang sheet builder, so
// it works off attributes as well as the original ids. They share the same radio
// ids deliberately: the two are never on screen together, and one script beats
// two that drift apart.

(function () {
    'use strict';

    var shipping = document.getElementById('fulfil-shipping');
    if (!shipping) return;

    var shippingOnly = [
        document.getElementById('ship-to'),
        document.getElementById('postage-line')
    ].filter(Boolean);

    document.querySelectorAll('[data-shipping-only]').forEach(function (element) {
        shippingOnly.push(element);
    });

    var pickupOnly = Array.prototype.slice.call(
        document.querySelectorAll('[data-pickup-only]'));

    document.querySelectorAll('[data-fulfilment]').forEach(function (radio) {
        radio.addEventListener('change', sync);
    });

    // Run once on load: a browser restoring a previously checked radio on a
    // back-navigation doesn't fire change, and the block would be out of step
    // with the choice it's meant to be showing.
    sync();

    function sync() {
        var posting = shipping.checked;

        shippingOnly.forEach(function (element) { element.hidden = !posting; });
        pickupOnly.forEach(function (element) { element.hidden = posting; });
    }
})();
