window.aiPanel = {
    _storageKey: 'nutrir_ai_panel_open',
    _wideKey: 'nutrir_ai_panel_wide',

    getStoredState: function () {
        try {
            return sessionStorage.getItem(this._storageKey) === 'true';
        } catch {
            return false;
        }
    },

    saveState: function (isOpen) {
        try {
            sessionStorage.setItem(this._storageKey, isOpen ? 'true' : 'false');
        } catch { }
    },

    getStoredWideState: function () {
        try {
            return localStorage.getItem(this._wideKey) === 'true';
        } catch {
            return false;
        }
    },

    saveWideState: function (isWide) {
        try {
            localStorage.setItem(this._wideKey, isWide ? 'true' : 'false');
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
