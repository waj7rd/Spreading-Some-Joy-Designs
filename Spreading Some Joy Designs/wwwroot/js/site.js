// Site-wide behaviour. Deliberately small — this is a server-rendered
// application, and anything that has to be right belongs on the server.

(function () {
    'use strict';

    // Confirm before anything destructive or hard to walk back. Opt in with
    // data-confirm="Are you sure?" on a form.
    document.addEventListener('submit', function (event) {
        var form = event.target;
        if (!(form instanceof HTMLFormElement)) return;

        var message = form.getAttribute('data-confirm');
        if (message && !window.confirm(message)) {
            event.preventDefault();
        }
    });
})();
