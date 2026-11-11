(() => {
    'use strict';
    document.querySelectorAll('[data-dismiss-notice]').forEach(button => {
        button.addEventListener('click', () => button.closest('.pen-notice').remove());
    });
    const menuButton = document.querySelector('[data-mobile-menu]');
    const navigation = document.getElementById('mobile-navigation');
    if (menuButton && navigation) {
        const closeMenu = () => { navigation.hidden = true; menuButton.setAttribute('aria-expanded', 'false'); };
        menuButton.addEventListener('click', () => {
            navigation.hidden = !navigation.hidden;
            menuButton.setAttribute('aria-expanded', String(!navigation.hidden));
        });
        document.addEventListener('keydown', event => {
            if (event.key === 'Escape' && !navigation.hidden) { closeMenu(); menuButton.focus(); }
        });
        document.addEventListener('click', event => {
            if (!navigation.hidden && !event.target.closest('.pen-mobile-header')) closeMenu();
        });
    }
    const dialog = document.getElementById('delete-confirmation');
    if (dialog && typeof dialog.showModal === 'function') {
        let pendingForm = null;
        let opener = null;
        let confirmed = false;
        const confirmButton = dialog.querySelector('[data-confirm-delete]');
        const openDialog = (form, button) => {
            if (dialog.open) return;
            pendingForm = form;
            opener = button;
            confirmed = false;
            confirmButton.disabled = false;
            dialog.querySelector('[data-delete-code]').textContent = form.dataset.taskCode;
            dialog.querySelector('[data-delete-title]').textContent = form.dataset.taskTitle;
            dialog.showModal();
            dialog.querySelector('[data-close-delete][autofocus]').focus();
        };
        document.querySelectorAll('.pen-delete-form').forEach(form => {
            const button = form.querySelector('[data-open-delete]');
            button.disabled = false;
            button.addEventListener('click', () => openDialog(form, button));
            form.addEventListener('submit', event => {
                if (!confirmed || form !== pendingForm) { event.preventDefault(); openDialog(form, button); }
            });
        });
        dialog.querySelectorAll('[data-close-delete]').forEach(button => button.addEventListener('click', () => dialog.close()));
        dialog.addEventListener('keydown', event => {
            if (event.key !== 'Tab') return;
            const buttons = [...dialog.querySelectorAll('button:not(:disabled)')];
            const first = buttons[0];
            const last = buttons[buttons.length - 1];
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
            }
        });
        dialog.addEventListener('close', () => { pendingForm = null; confirmed = false; opener?.focus(); });
        confirmButton.addEventListener('click', () => {
            if (!pendingForm || confirmed) return;
            confirmed = true;
            confirmButton.disabled = true;
            pendingForm.requestSubmit();
        });
    }
    document.querySelector('[data-validation-alert]')?.focus();
})();
