window.aiPanel = {
    _storageKey: 'nutrir_ai_panel_open',
    _wideKey: 'nutrir_ai_panel_wide',
    _dotNetRef: null,
    _debounceTimer: null,
    _currentValue: '',
    _boundHandleInput: null,

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
    },

    initInputHandler: function (dotNetRef) {
        this._dotNetRef = dotNetRef;
        this._currentValue = '';
        var self = this;
        this._boundHandleInput = function (e) {
            self._currentValue = e.target.value;
            if (self._debounceTimer) {
                clearTimeout(self._debounceTimer);
            }
            self._debounceTimer = setTimeout(function () {
                if (self._dotNetRef) {
                    self._dotNetRef.invokeMethodAsync('OnInputTextChanged', self._currentValue);
                }
            }, 200);
        };
        var input = document.getElementById('ai-input');
        if (input) {
            input.addEventListener('input', this._boundHandleInput);
        }
    },

    getCurrentInputValue: function () {
        var input = document.getElementById('ai-input');
        return input ? input.value : '';
    },

    setInputValue: function (value) {
        var input = document.getElementById('ai-input');
        if (input) {
            input.value = value;
        }
        this._currentValue = value;
    },

    flushInput: function () {
        if (this._debounceTimer) {
            clearTimeout(this._debounceTimer);
            this._debounceTimer = null;
        }
        return this._currentValue;
    },

    disposeInputHandler: function () {
        var input = document.getElementById('ai-input');
        if (input && this._boundHandleInput) {
            input.removeEventListener('input', this._boundHandleInput);
        }
        if (this._debounceTimer) {
            clearTimeout(this._debounceTimer);
        }
        this._dotNetRef = null;
        this._currentValue = '';
        this._boundHandleInput = null;
    }
};
