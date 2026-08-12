// Drag and drop rescheduling for the staff schedule board.
//
// Written against the native HTML5 drag and drop API rather than a library:
// the assignment awards more credit for code we write ourselves, and the whole
// behaviour is about eighty lines.
//
// The server is the authority. A drop optimistically moves the block, posts the
// move, and puts the block back if the server refuses it.
(function () {
    'use strict';

    var board = document.querySelector('.board');
    if (!board) return;

    var msg = document.getElementById('board-msg');
    var tokenField = document.querySelector('input[name="__RequestVerificationToken"]');
    var dragged = null;
    var origin = null;

    function say(text, isError) {
        if (!msg) return;
        msg.textContent = text;
        msg.className = 'boardmsg' + (isError ? ' error' : ' ok');
        msg.hidden = false;
        clearTimeout(say.timer);
        say.timer = setTimeout(function () { msg.hidden = true; }, 5000);
    }

    // Remember where a block started, so a refused move can be undone.
    function snapshot(el) {
        return {
            gridRow: el.style.gridRow,
            gridColumn: el.style.gridColumn
        };
    }

    function restore(el, snap) {
        el.style.gridRow = snap.gridRow;
        el.style.gridColumn = snap.gridColumn;
    }

    board.addEventListener('dragstart', function (e) {
        var item = e.target.closest('.board-item');
        if (!item || item.getAttribute('draggable') !== 'true') return;

        dragged = item;
        origin = snapshot(item);
        item.classList.add('dragging');

        // Firefox will not start a drag without data on the transfer.
        e.dataTransfer.setData('text/plain', item.dataset.item);
        e.dataTransfer.effectAllowed = 'move';
    });

    board.addEventListener('dragend', function () {
        if (dragged) dragged.classList.remove('dragging');
        clearHover();
    });

    function clearHover() {
        var cells = board.querySelectorAll('.board-cell.hover');
        for (var i = 0; i < cells.length; i++) cells[i].classList.remove('hover');
    }

    board.addEventListener('dragover', function (e) {
        var cell = e.target.closest('.board-cell');
        if (!cell || !dragged) return;

        // Without preventDefault the browser refuses the drop.
        e.preventDefault();
        e.dataTransfer.dropEffect = 'move';

        clearHover();
        cell.classList.add('hover');
    });

    board.addEventListener('drop', function (e) {
        var cell = e.target.closest('.board-cell');
        if (!cell || !dragged) return;

        e.preventDefault();
        clearHover();

        var item = dragged;
        var snap = origin;
        var span = (item.style.gridRow.split('span')[1] || '1').trim();
        var targetRow = cell.style.gridRow;

        // Move it immediately so the drag feels responsive, then confirm.
        item.style.gridColumn = cell.style.gridColumn;
        item.style.gridRow = targetRow + ' / span ' + span;
        item.classList.add('pending');

        var body = new URLSearchParams();
        body.set('itemId', item.dataset.item);
        body.set('staffEmail', cell.dataset.staff);
        body.set('slotStart', cell.dataset.slot);
        if (tokenField) body.set('__RequestVerificationToken', tokenField.value);

        fetch('/Appointment/Reschedule', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
                'X-Requested-With': 'XMLHttpRequest'
            },
            body: body.toString()
        })
        .then(function (r) { return r.json(); })
        .then(function (data) {
            item.classList.remove('pending');

            if (data && data.ok) {
                say(data.message, false);
                // Reload so the block's label and the audit trail stay truthful.
                setTimeout(function () { location.reload(); }, 600);
            } else {
                restore(item, snap);
                say((data && data.error) || 'That move was refused.', true);
            }
        })
        .catch(function () {
            item.classList.remove('pending');
            restore(item, snap);
            say('Could not reach the server. The booking was not moved.', true);
        });

        dragged = null;
    });
})();
