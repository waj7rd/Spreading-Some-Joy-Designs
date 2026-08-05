// Drag-and-resize for artwork on the shirt.
//
// The number boxes are still there and still work — they're the accessible,
// precise path, and someone on a keyboard has to be able to place artwork too.
// This layer drives the same inputs, so both stay in step and the form posts
// exactly what it always did. Nothing here is a source of truth: the hidden
// millimetre values are, and the server re-checks all of it regardless.
//
// No library. Pointer events cover mouse, touch and pen in one set of handlers.

(function () {
    'use strict';

    var MM_PER_INCH = 25.4;

    document.querySelectorAll('.sj-shirt.is-interactive').forEach(setUpSide);

    function setUpSide(shirt) {
        var wrap = shirt.querySelector('.sj-artwork-wrap');
        var area = shirt.querySelector('.sj-print-area');
        if (!wrap || !area) return;

        var img = wrap.querySelector('.sj-artwork');
        var handle = wrap.querySelector('.sj-resize-handle');

        var side = shirt.dataset.side;                       // "front" / "back"
        var field = side.charAt(0).toUpperCase() + side.slice(1); // "Front" / "Back"

        var areaWmm = num(area.dataset.widthMm);
        var areaHmm = num(area.dataset.heightMm);
        var pxW = num(img.dataset.pxW);
        var pxH = num(img.dataset.pxH);
        var minMm = num(shirt.dataset.minMm) || 20;
        var minDpi = num(shirt.dataset.minDpi) || 150;

        if (!areaWmm || !areaHmm) return;

        var inputs = {
            x: document.querySelector('[name="' + field + '.XMm"]'),
            y: document.querySelector('[name="' + field + '.YMm"]'),
            w: document.querySelector('[name="' + field + '.WidthMm"]'),
            h: document.querySelector('[name="' + field + '.HeightMm"]')
        };

        if (!inputs.x || !inputs.y || !inputs.w || !inputs.h) return;

        var readout = document.querySelector('[data-readout="' + field + '"]');

        // Aspect comes from the image's own pixels, so resizing can never
        // squash the artwork — only the server-side placement rules decide
        // whether the result is printable.
        var aspect = (pxW > 0 && pxH > 0) ? pxH / pxW : 1;

        var state = {
            x: num(inputs.x.value),
            y: num(inputs.y.value),
            w: num(inputs.w.value),
            h: num(inputs.h.value)
        };

        // ---- drawing ----

        function mmPerPx() {
            var rect = area.getBoundingClientRect();
            return rect.width > 0 ? areaWmm / rect.width : 0;
        }

        function render() {
            wrap.style.left = pct(state.x, areaWmm);
            wrap.style.top = pct(state.y, areaHmm);
            wrap.style.width = pct(state.w, areaWmm);
            wrap.style.height = pct(state.h, areaHmm);

            inputs.x.value = state.x;
            inputs.y.value = state.y;
            inputs.w.value = state.w;
            inputs.h.value = state.h;

            updateReadout();
        }

        // Live DPI, so the customer finds out the print is getting soft while
        // they're dragging rather than when the save is refused.
        function updateReadout() {
            if (!readout) return;

            var dpi = effectiveDpi();
            var tooLow = dpi < minDpi;

            readout.textContent = state.w + ' × ' + state.h + ' mm · ' + dpi + ' DPI';
            readout.classList.toggle('is-low', tooLow);
            readout.title = tooLow
                ? 'Below ' + minDpi + ' DPI — this would print soft. Make it smaller, or use a larger image.'
                : 'Good to print.';
        }

        function effectiveDpi() {
            if (!pxW || !pxH || state.w <= 0 || state.h <= 0) return 0;

            return Math.floor(Math.min(
                pxW / (state.w / MM_PER_INCH),
                pxH / (state.h / MM_PER_INCH)));
        }

        // ---- dragging ----

        wrap.addEventListener('pointerdown', function (e) {
            if (handle && e.target === handle) return;   // resizing, not moving
            e.preventDefault();

            var scale = mmPerPx();
            var startX = e.clientX, startY = e.clientY;
            var originX = state.x, originY = state.y;

            track(e, function (ev) {
                state.x = clamp(Math.round(originX + (ev.clientX - startX) * scale), 0, areaWmm - state.w);
                state.y = clamp(Math.round(originY + (ev.clientY - startY) * scale), 0, areaHmm - state.h);
                render();
            });
        });

        // ---- resizing ----

        if (handle) {
            handle.addEventListener('pointerdown', function (e) {
                e.preventDefault();
                e.stopPropagation();

                var scale = mmPerPx();
                var startX = e.clientX;
                var originW = state.w;

                // The smallest width whose *height* also clears the minimum.
                // Clamping width alone is wrong for a landscape image: 20mm
                // wide makes it 15mm tall, which the press won't run — and the
                // naive version simply refused to shrink at all rather than
                // stopping at the smallest size that works.
                var minWidth = Math.ceil(Math.max(minMm, minMm / aspect));

                track(e, function (ev) {
                    var width = Math.round(originW + (ev.clientX - startX) * scale);

                    // Can't run off the right edge, can't go below the smallest
                    // print the press will do.
                    width = clamp(width, minWidth, areaWmm - state.x);

                    var height = Math.round(width * aspect);

                    // Tall artwork hits the bottom of the print area first, so
                    // height is what constrains it — resolve back to a width.
                    if (state.y + height > areaHmm) {
                        height = areaHmm - state.y;
                        width = Math.round(height / aspect);
                    }

                    if (width < minMm || height < minMm) return;

                    state.w = width;
                    state.h = height;
                    render();
                });
            });
        }

        // Pointer capture keeps the drag alive when the cursor leaves the
        // element — otherwise a quick movement drops it mid-gesture.
        function track(downEvent, onMove) {
            var target = downEvent.currentTarget;

            // Throws if the pointer is no longer active — a fast click that's
            // already released by the time this runs. Losing capture only means
            // the drag ends early if the cursor leaves the element; an uncaught
            // throw here would stop dragging working at all.
            try {
                target.setPointerCapture(downEvent.pointerId);
            } catch (err) { /* carry on without capture */ }

            shirt.classList.add('is-dragging');

            function move(ev) { onMove(ev); }

            function up() {
                try {
                    target.releasePointerCapture(downEvent.pointerId);
                } catch (err) { /* already gone */ }

                shirt.classList.remove('is-dragging');
                target.removeEventListener('pointermove', move);
                target.removeEventListener('pointerup', up);
                target.removeEventListener('pointercancel', up);
            }

            target.addEventListener('pointermove', move);
            target.addEventListener('pointerup', up);
            target.addEventListener('pointercancel', up);
        }

        // ---- typing in the boxes still works ----

        Object.keys(inputs).forEach(function (key) {
            inputs[key].addEventListener('input', function () {
                state.w = clamp(num(inputs.w.value), minMm, areaWmm);
                state.h = clamp(num(inputs.h.value), minMm, areaHmm);
                state.x = clamp(num(inputs.x.value), 0, areaWmm - state.w);
                state.y = clamp(num(inputs.y.value), 0, areaHmm - state.h);
                render();
            });
        });

        // Nudge with the arrow keys once the artwork has focus — the keyboard
        // equivalent of dragging.
        wrap.setAttribute('tabindex', '0');
        wrap.addEventListener('keydown', function (e) {
            var step = e.shiftKey ? 10 : 1;
            var moved = true;

            switch (e.key) {
                case 'ArrowLeft':  state.x = clamp(state.x - step, 0, areaWmm - state.w); break;
                case 'ArrowRight': state.x = clamp(state.x + step, 0, areaWmm - state.w); break;
                case 'ArrowUp':    state.y = clamp(state.y - step, 0, areaHmm - state.h); break;
                case 'ArrowDown':  state.y = clamp(state.y + step, 0, areaHmm - state.h); break;
                default: moved = false;
            }

            if (moved) {
                e.preventDefault();
                render();
            }
        });

        // The stored size may pre-date a print-area change, so normalise once
        // on load rather than waiting for the first drag to correct it.
        render();
    }

    // ---- helpers ----

    function num(value) {
        var n = parseInt(value, 10);
        return isNaN(n) ? 0 : n;
    }

    function clamp(value, min, max) {
        if (max < min) return min;
        return Math.min(Math.max(value, min), max);
    }

    function pct(value, total) {
        return total > 0 ? (value * 100 / total).toFixed(3) + '%' : '0%';
    }
})();
