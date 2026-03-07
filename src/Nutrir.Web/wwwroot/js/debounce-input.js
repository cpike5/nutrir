window.debounceInput = {
    _handlers: {},

    register: function (elementId, dotNetRef, debounceMs) {
        var element = document.getElementById(elementId);
        if (!element) return;

        // Clean up any existing handler
        this.unregister(elementId);

        var self = this;
        var handler = function (e) {
            var entry = self._handlers[elementId];
            if (entry) {
                if (entry.timer) clearTimeout(entry.timer);
                entry.timer = setTimeout(function () {
                    dotNetRef.invokeMethodAsync('OnDebouncedInput', e.target.value);
                }, debounceMs);
            }
        };

        element.addEventListener('input', handler);
        this._handlers[elementId] = { handler: handler, element: element, timer: null };
    },

    unregister: function (elementId) {
        var entry = this._handlers[elementId];
        if (entry) {
            if (entry.timer) clearTimeout(entry.timer);
            entry.element.removeEventListener('input', entry.handler);
            delete this._handlers[elementId];
        }
    }
};
