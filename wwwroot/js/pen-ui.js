(() => {
    'use strict';
    const themeButtons = document.querySelectorAll('[data-theme-choice]');
    const themeSelectors = document.querySelectorAll('[data-theme-selector]');
    const themeMedia = window.matchMedia('(prefers-color-scheme: dark)');
    let themePreference = null;
    try { themePreference = localStorage.getItem('workflow-theme'); } catch { /* Depolama erişimi isteğe bağlıdır. */ }
    if (themePreference !== 'light' && themePreference !== 'dark') themePreference = null;
    const applyTheme = theme => {
        document.documentElement.dataset.theme = theme;
        themeButtons.forEach(button => {
            button.setAttribute('aria-pressed', String(button.dataset.themeChoice === theme));
        });
    };
    applyTheme(themePreference || (themeMedia.matches ? 'dark' : 'light'));
    themeSelectors.forEach(selector => { selector.hidden = false; });
    themeButtons.forEach(button => {
        button.addEventListener('click', () => {
            themePreference = button.dataset.themeChoice;
            applyTheme(themePreference);
            try { localStorage.setItem('workflow-theme', themePreference); } catch { /* Oturum boyunca seçim korunur. */ }
        });
    });
    themeMedia.addEventListener?.('change', event => { if (!themePreference) applyTheme(event.matches ? 'dark' : 'light'); });
    document.querySelectorAll('[data-dismiss-notice]').forEach(button => {
        button.addEventListener('click', () => button.closest('.pen-notice').remove());
    });
    const menuButton = document.querySelector('[data-mobile-menu]');
    const navigation = document.getElementById('mobile-navigation');
    if (menuButton && navigation) {
        const closeMenu = () => { navigation.hidden = true; menuButton.setAttribute('aria-expanded', 'false'); menuButton.setAttribute('aria-label', 'Gezinme menüsünü aç'); };
        menuButton.addEventListener('click', () => {
            navigation.hidden = !navigation.hidden;
            menuButton.setAttribute('aria-expanded', String(!navigation.hidden));
            menuButton.setAttribute('aria-label', navigation.hidden ? 'Gezinme menüsünü aç' : 'Gezinme menüsünü kapat');
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
    const board = document.querySelector('[data-board-view]');
    const viewControls = document.querySelector('[data-view-controls]');
    const list = document.querySelector('[data-list-view]');
    const currentView = document.querySelector('[data-current-view]')?.dataset.currentView;
    if (viewControls && currentView) {
        const url = new URL(window.location.href);
        let savedView = null;
        try { savedView = localStorage.getItem('workflow-task-view'); } catch { /* Depolama isteğe bağlıdır. */ }
        if (!url.searchParams.has('view') && savedView === 'board' && currentView === 'list') {
            url.searchParams.set('view', 'board');
            window.location.replace(url.href);
        } else {
            try { localStorage.setItem('workflow-task-view', currentView); } catch { /* Depolama isteğe bağlıdır. */ }
        }
        viewControls.querySelectorAll('[data-task-view]').forEach(link => link.addEventListener('click', () => {
            try { localStorage.setItem('workflow-task-view', link.dataset.taskView); } catch { /* Bağlantı yine de çalışır. */ }
        }));
        window.addEventListener('pageshow', event => {
            if (event.persisted) {
                try { localStorage.setItem('workflow-task-view', currentView); } catch { /* Depolama isteğe bağlıdır. */ }
            }
        });
    }
    if (board) {

        const message = board.querySelector('[data-board-message]');
        const token = board.querySelector('[name="__RequestVerificationToken"]').value;
        const columns = [...board.querySelectorAll('[data-board-column]')];
        const updateCounts = () => columns.forEach(column => {
            const count = column.querySelectorAll('[data-board-card]').length;
            column.querySelector('[data-board-count]').textContent = count;
            column.querySelector('[data-board-empty]').hidden = count !== 0;
        });
        const announce = (text, error = false) => { message.textContent = text; message.dataset.error = String(error); };
        const moveTask = async (card, newStatus) => {
            const oldStatus = card.dataset.status;
            const select = card.querySelector('[data-board-status]');
            const restoreFocus = document.activeElement === select;
            if (card.dataset.pending === 'true') { select.value = oldStatus; return; }
            if (oldStatus === newStatus) return;
            const oldParent = card.parentElement;
            const oldNext = card.nextElementSibling;
            const target = columns.find(column => column.dataset.boardColumn === newStatus);
            if (!target) { select.value = oldStatus; return; }
            card.dataset.pending = 'true';
            card.setAttribute('aria-busy', 'true');
            card.draggable = false;
            select.disabled = true;
            target.querySelector('[data-board-cards]').append(card);
            select.value = newStatus;
            updateCounts();
            announce('Görev durumu güncelleniyor.');
            let failureMessage = 'Görev durumu güncellenemedi. Lütfen tekrar deneyin.';
            const controller = new AbortController();
            const timeout = window.setTimeout(() => controller.abort(), 15000);
            try {
                const data = new FormData();
                data.append('gorevId', card.dataset.taskId);
                data.append('yeniDurum', newStatus);
                data.append('__RequestVerificationToken', token);
                const response = await fetch(board.dataset.updateUrl, { method: 'POST', body: data, signal: controller.signal, headers: { 'Accept': 'application/json' } });
                let result;
                try { result = await response.json(); } catch { /* CSRF ve beklenmeyen yanıtlar genel hataya dönüşür. */ }
                if (!response.ok || !result || result.success !== true) {
                    if (typeof result?.message === 'string' && result.message) failureMessage = result.message;
                    throw new Error('update failed');
                }
                card.dataset.status = newStatus;
                const row = list ? [...list.querySelectorAll('[data-list-task]')].find(item => item.dataset.listTask === card.dataset.taskId) : null;
                const listSelect = row?.querySelector('select[name="yeniDurum"]');
                if (listSelect) {
                    const statusNames = ['Todo', 'InProgress', 'Completed'];
                    listSelect.value = statusNames[Number(newStatus)];
                    listSelect.className = `status-${statusNames[Number(newStatus)].toLowerCase()}`;
                }
                [card, row].filter(Boolean).forEach(element => element.querySelectorAll('[data-due-indicator]').forEach(indicator => { indicator.hidden = newStatus === '2'; }));
                const oldSummary = document.querySelector(`[data-summary-status="${oldStatus}"]`);
                const newSummary = document.querySelector(`[data-summary-status="${newStatus}"]`);
                if (oldSummary && newSummary) {
                    oldSummary.textContent = Math.max(0, Number(oldSummary.textContent) - 1);
                    newSummary.textContent = Number(newSummary.textContent) + 1;
                }
                announce(result.message || 'Görev durumu güncellendi.');
            } catch (error) {
                oldParent.insertBefore(card, oldNext && oldNext.parentElement === oldParent ? oldNext : null);
                select.value = oldStatus;
                if (error.name === 'AbortError') failureMessage = 'İstek zaman aşımına uğradı. Durumu kontrol etmek için sayfayı yenileyin.';
                announce(failureMessage, true);
            } finally {
                window.clearTimeout(timeout);
                delete card.dataset.pending;
                card.removeAttribute('aria-busy');
                card.draggable = true;
                select.disabled = false;
                updateCounts();
                if (restoreFocus && !board.hidden && document.activeElement === document.body) select.focus();
            }
        };
        board.querySelectorAll('[data-board-status]').forEach(select => select.addEventListener('change', () => moveTask(select.closest('[data-board-card]'), select.value)));
        let dragged = null;
        board.querySelectorAll('[data-board-card]').forEach(card => {
            card.addEventListener('dragstart', event => {
                if (card.dataset.pending === 'true' || event.target.closest('select, a')) { event.preventDefault(); return; }
                dragged = card;
                event.dataTransfer.effectAllowed = 'move';
                event.dataTransfer.setData('text/plain', card.dataset.taskId);
                card.classList.add('is-dragging');
            });
            card.addEventListener('dragend', () => { dragged = null; card.classList.remove('is-dragging'); columns.forEach(column => column.classList.remove('is-drop-target')); });
        });
        columns.forEach(column => {
            column.addEventListener('dragover', event => { if (dragged) { event.preventDefault(); event.dataTransfer.dropEffect = 'move'; column.classList.add('is-drop-target'); } });
            column.addEventListener('dragleave', event => { if (!column.contains(event.relatedTarget)) column.classList.remove('is-drop-target'); });
            column.addEventListener('drop', event => { if (!dragged) return; event.preventDefault(); column.classList.remove('is-drop-target'); moveTask(dragged, column.dataset.boardColumn); });
        });
    }
    document.querySelector('[data-validation-alert]')?.focus();
})();
