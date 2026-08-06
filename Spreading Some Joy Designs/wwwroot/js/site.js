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

    // Submit on change, for selects that act as navigation.
    // data-submit-on-change rather than an inline onchange, because the
    // Content-Security-Policy blocks inline handlers.
    document.addEventListener('change', function (event) {
        var el = event.target;
        if (el && el.hasAttribute && el.hasAttribute('data-submit-on-change') && el.form) {
            el.form.submit();
        }
    });

    // Show the text wordmark if the logo image is missing.
    //
    // Bound per-element rather than delegated: 'error' on an <img> doesn't
    // bubble, so a document-level listener would never see it. Also has to be
    // attached before the image finishes loading — hence checking complete,
    // which catches an image that already failed from cache.
    Array.prototype.forEach.call(
        document.querySelectorAll('img[data-fallback]'),
        function (img) {
            function showFallback() {
                img.style.display = 'none';
                var fallback = document.getElementById(img.getAttribute('data-fallback'));
                if (fallback) fallback.style.display = 'block';
            }

            img.addEventListener('error', showFallback);

            if (img.complete && img.naturalWidth === 0) showFallback();
        });
})();
