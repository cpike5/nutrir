window.globalSearch = {
    _dotNetRef: null,
    _keyHandler: null,
    _storageKey: 'nutrir_recent_searches',

    init: function (dotNetRef) {
        this._dotNetRef = dotNetRef;
        this._keyHandler = function (e) {
            if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
                e.preventDefault();
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('OnSearchShortcut');
                }
            }
        };
        document.addEventListener('keydown', this._keyHandler);
    },

    dispose: function () {
        if (this._keyHandler) {
            document.removeEventListener('keydown', this._keyHandler);
            this._keyHandler = null;
        }
        this._dotNetRef = null;
    },

    focusInput: function (inputId) {
        var el = document.getElementById(inputId);
        if (el) el.focus();
    },

    getRecentSearches: function () {
        try {
            var data = localStorage.getItem(this._storageKey);
            return data ? JSON.parse(data) : [];
        } catch {
            return [];
        }
    },

    addRecentSearch: function (item) {
        try {
            var searches = this.getRecentSearches();
            // Remove duplicate by url
            searches = searches.filter(function (s) { return s.url !== item.url; });
            searches.unshift(item);
            if (searches.length > 10) searches = searches.slice(0, 10);
            localStorage.setItem(this._storageKey, JSON.stringify(searches));
        } catch { }
    },

    clearRecentSearches: function () {
        try {
            localStorage.removeItem(this._storageKey);
        } catch { }
    }
};
