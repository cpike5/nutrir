window.aiPanel = {
    _storageKey: 'nutrir_ai_panel_open',

    getStoredState: function () {
        try {
            return localStorage.getItem(this._storageKey) === 'true';
        } catch {
            return false;
        }
    },

    saveState: function (isOpen) {
        try {
            localStorage.setItem(this._storageKey, isOpen ? 'true' : 'false');
        } catch { }
    },

    scrollToBottom: function (elementId) {
        var el = document.getElementById(elementId);
        if (el) {
            el.scrollTop = el.scrollHeight;
        }
    },

    focusInput: function (elementId) {
        var el = document.getElementById(elementId);
        if (el) {
            el.focus();
        }
    }
};
