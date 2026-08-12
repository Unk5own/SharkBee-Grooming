// Before and after comparison slider.
//
// Two stacked images; the top one is clipped by a draggable divider. Works with
// mouse, touch and the keyboard, and any element that already has the two image
// URLs on it becomes a slider.
(function () {
    'use strict';

    var boxes = document.querySelectorAll('.ba');
    if (!boxes.length) return;

    for (var i = 0; i < boxes.length; i++) build(boxes[i]);

    function build(box) {
        var beforeSrc = box.dataset.before;
        var afterSrc = box.dataset.after;
        if (!beforeSrc || !afterSrc) return;

        box.innerHTML =
            '<img class="ba-img ba-before" src="' + beforeSrc + '" alt="Before grooming">' +
            '<div class="ba-clip"><img class="ba-img" src="' + afterSrc + '" alt="After grooming"></div>' +
            '<div class="ba-handle" role="slider" tabindex="0" aria-label="Compare before and after"' +
            ' aria-valuemin="0" aria-valuemax="100" aria-valuenow="50"></div>' +
            '<span class="ba-tag ba-tag-l">Before</span>' +
            '<span class="ba-tag ba-tag-r">After</span>';

        var clip = box.querySelector('.ba-clip');
        var handle = box.querySelector('.ba-handle');
        var dragging = false;

        function setPercent(p) {
            p = Math.max(0, Math.min(100, p));
            clip.style.width = p + '%';
            handle.style.left = p + '%';
            handle.setAttribute('aria-valuenow', Math.round(p));
        }

        function fromEvent(e) {
            var rect = box.getBoundingClientRect();
            var x = (e.touches ? e.touches[0].clientX : e.clientX) - rect.left;
            setPercent((x / rect.width) * 100);
        }

        setPercent(50);

        box.addEventListener('mousedown', function (e) { dragging = true; fromEvent(e); e.preventDefault(); });
        window.addEventListener('mousemove', function (e) { if (dragging) fromEvent(e); });
        window.addEventListener('mouseup', function () { dragging = false; });

        box.addEventListener('touchstart', function (e) { dragging = true; fromEvent(e); }, { passive: true });
        box.addEventListener('touchmove', function (e) { if (dragging) fromEvent(e); }, { passive: true });
        window.addEventListener('touchend', function () { dragging = false; });

        handle.addEventListener('keydown', function (e) {
            var now = parseFloat(handle.getAttribute('aria-valuenow')) || 50;
            if (e.key === 'ArrowLeft') { setPercent(now - 4); e.preventDefault(); }
            if (e.key === 'ArrowRight') { setPercent(now + 4); e.preventDefault(); }
        });
    }
})();
