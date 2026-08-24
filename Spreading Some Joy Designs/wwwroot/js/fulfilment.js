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

(function () {
    'use strict';

    var shipping = document.getElementById('fulfil-shipping');
    if (!shipping) return;

    var toggled = [
        document.getElementById('ship-to'),
        document.getElementById('postage-line')
    ];

    document.querySelectorAll('[data-fulfilment]').forEach(function (radio) {
        radio.addEventListener('change', sync);
    });

    // Run once on load: a browser restoring a previously checked radio on a
    // back-navigation doesn't fire change, and the block would be out of step
    // with the choice it's meant to be showing.
    sync();

    function sync() {
        toggled.forEach(function (element) {
            if (element) element.hidden = !shipping.checked;
        });
    }
})();
