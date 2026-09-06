// Wires up every ".pwd-toggle" button on the page: each toggle flips the
// type (password <-> text) of the sibling input named in its data-toggle
// attribute. Shared across Login, Register, ForgotPassword and ResetPassword
// so the behaviour and label wording stay consistent in one place.
document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('.pwd-toggle').forEach(btn => {
        btn.addEventListener('click', () => {
            const input = btn.parentElement.querySelector('input');
            if (!input) return;

            const showing = input.type === 'text';
            input.type = showing ? 'password' : 'text';
            btn.textContent = showing ? 'Show' : 'Hide';
        });
    });
});
