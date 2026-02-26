/**
 * Context Menu with Prediction Cone (Safe Triangle) Algorithm
 *
 * All mouse tracking, cone geometry, submenu toggling, and positioning
 * stays in JS for performance. Blazor owns the data model and action dispatch.
 */
'use strict';

window.contextMenu = {
    // ── State ────────────────────────────────────────────────────────
    _dotNetRef: null,
    _rootEl: null,
    _mouse: { x: 0, y: 0 },
    _cone: {
        active: false,
        apex: null,
        submenuEl: null,
        flipped: false,
        depth: -1
    },
    _openSubmenus: [],
    _hoverSwitchTimer: null,
    _mouseMoveHandler: null,
    _clickOutsideHandler: null,
    _keydownHandler: null,
    _focusedIndex: -1,
    _focusedDepth: 0,

    // ── Lifecycle ────────────────────────────────────────────────────

    init: function (dotNetRef, rootElement) {
        this._dotNetRef = dotNetRef;
        this._rootEl = rootElement;

        this._mouseMoveHandler = this._onMouseMove.bind(this);
        this._clickOutsideHandler = this._onClickOutside.bind(this);
        this._keydownHandler = this._onKeydown.bind(this);

        document.addEventListener('mousemove', this._mouseMoveHandler);
        document.addEventListener('mousedown', this._clickOutsideHandler);
        document.addEventListener('keydown', this._keydownHandler);

        this._bindMenuItems();
    },

    dispose: function () {
        if (this._mouseMoveHandler) {
            document.removeEventListener('mousemove', this._mouseMoveHandler);
            this._mouseMoveHandler = null;
        }
        if (this._clickOutsideHandler) {
            document.removeEventListener('mousedown', this._clickOutsideHandler);
            this._clickOutsideHandler = null;
        }
        if (this._keydownHandler) {
            document.removeEventListener('keydown', this._keydownHandler);
            this._keydownHandler = null;
        }
        clearTimeout(this._hoverSwitchTimer);
        this._dotNetRef = null;
        this._rootEl = null;
        this._openSubmenus = [];
        this._cone.active = false;
    },

    rebind: function (rootElement) {
        this._rootEl = rootElement;
        this._bindMenuItems();
    },

    // ── Public API ───────────────────────────────────────────────────

    open: function (x, y) {
        if (!this._rootEl) return;

        this._closeAllSubmenus();
        this._rootEl.classList.add('open');

        var mw = this._rootEl.offsetWidth || 240;
        var mh = this._rootEl.offsetHeight || 400;
        var vpW = window.innerWidth;
        var vpH = window.innerHeight;

        var cx = x;
        var cy = y;
        if (cx + mw > vpW - 8) cx = vpW - mw - 8;
        if (cy + mh > vpH - 8) cy = vpH - mh - 8;
        if (cx < 8) cx = 8;
        if (cy < 8) cy = 8;

        this._rootEl.style.left = cx + 'px';
        this._rootEl.style.top = cy + 'px';

        this._focusedIndex = -1;
        this._focusedDepth = 0;
    },

    close: function () {
        if (!this._rootEl) return;
        this._closeAllSubmenus();
        this._rootEl.classList.remove('open');
        this._clearAllHovers();
        this._focusedIndex = -1;
    },

    // ── Geometry: Point-in-Triangle ──────────────────────────────────

    _cross2d: function (ax, ay, bx, by, px, py) {
        return (bx - ax) * (py - ay) - (by - ay) * (px - ax);
    },

    _pointInTriangle: function (px, py, ax, ay, bx, by, cx, cy) {
        var d1 = this._cross2d(ax, ay, bx, by, px, py);
        var d2 = this._cross2d(bx, by, cx, cy, px, py);
        var d3 = this._cross2d(cx, cy, ax, ay, px, py);

        var hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        var hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);

        return !(hasNeg && hasPos);
    },

    // ── Cone Management ──────────────────────────────────────────────

    _activateCone: function (submenuWrapper, flipped, depth) {
        this._cone.active = true;
        this._cone.apex = { x: this._mouse.x, y: this._mouse.y };
        this._cone.submenuEl = submenuWrapper;
        this._cone.flipped = flipped;
        this._cone.depth = depth;
    },

    _deactivateCone: function () {
        this._cone.active = false;
        this._cone.submenuEl = null;
    },

    _mouseInCone: function () {
        var c = this._cone;
        if (!c.active || !c.submenuEl || !c.apex) return false;

        var rect = c.submenuEl.getBoundingClientRect();
        if (rect.width === 0) return false;

        var cornerTopX, cornerBotX;
        if (c.flipped) {
            cornerTopX = rect.right;
            cornerBotX = rect.right;
        } else {
            cornerTopX = rect.left;
            cornerBotX = rect.left;
        }

        return this._pointInTriangle(
            this._mouse.x, this._mouse.y,
            c.apex.x, c.apex.y,
            cornerTopX, rect.top,
            cornerBotX, rect.bottom
        );
    },

    // ── Submenu State ────────────────────────────────────────────────

    _closeSubmenusFrom: function (depth) {
        for (var i = this._openSubmenus.length - 1; i >= depth; i--) {
            var entry = this._openSubmenus[i];
            entry.wrapper.classList.remove('open');
            entry.item.classList.remove('active-submenu');
            entry.item.setAttribute('aria-expanded', 'false');
        }
        this._openSubmenus = this._openSubmenus.slice(0, depth);

        if (this._cone.active && this._cone.depth >= depth) {
            this._deactivateCone();
        }
    },

    _closeAllSubmenus: function () {
        this._closeSubmenusFrom(0);
    },

    _openSubmenu: function (item, depth) {
        this._closeSubmenusFrom(depth);

        var wrapper = null;
        var children = item.children;
        for (var i = 0; i < children.length; i++) {
            if (children[i].classList && children[i].classList.contains('submenu-wrapper')) {
                wrapper = children[i];
                break;
            }
        }
        if (!wrapper) return;

        var itemRect = item.getBoundingClientRect();
        var vpWidth = window.innerWidth;
        var vpHeight = window.innerHeight;

        // Temporarily show to measure
        wrapper.style.display = 'block';
        var wRect = wrapper.getBoundingClientRect();
        wrapper.style.display = '';

        // Horizontal: flip left if not enough space on the right
        var flipped = (itemRect.right + wRect.width + 8) > vpWidth;
        if (flipped) {
            wrapper.classList.add('flip-left');
        } else {
            wrapper.classList.remove('flip-left');
        }

        // Vertical: shift up if it would overflow the bottom
        var overflowBottom = (itemRect.top + wRect.height) - vpHeight;
        if (overflowBottom > 0) {
            wrapper.style.top = (-overflowBottom - 4) + 'px';
        } else {
            wrapper.style.top = '0px';
        }

        wrapper.classList.add('open');
        item.classList.add('active-submenu');
        item.setAttribute('aria-expanded', 'true');

        this._openSubmenus.push({ item: item, wrapper: wrapper });
        this._activateCone(wrapper, flipped, depth);
    },

    // ── Item Hover ───────────────────────────────────────────────────

    _getItemDepth: function (item) {
        var depth = 0;
        var el = item.parentElement;
        while (el && el !== this._rootEl) {
            if (el.classList.contains('menu-list')) depth++;
            el = el.parentElement;
        }
        return depth - 1;
    },

    _onItemMouseEnter: function (e) {
        var item = e.currentTarget;
        var depth = this._getItemDepth(item);
        var self = this;

        clearTimeout(this._hoverSwitchTimer);

        var coneIsActive = this._cone.active && depth === this._cone.depth && this._mouseInCone();

        if (coneIsActive) {
            this._hoverSwitchTimer = setTimeout(function () {
                if (!self._cone.active || !self._mouseInCone()) {
                    self._applyItemHover(item, depth);
                }
            }, 80);
            return;
        }

        this._applyItemHover(item, depth);
    },

    _applyItemHover: function (item, depth) {
        var siblings = item.parentElement.querySelectorAll(':scope > .menu-item');
        for (var i = 0; i < siblings.length; i++) {
            siblings[i].classList.remove('hovered');
        }
        item.classList.add('hovered');

        if (item.classList.contains('has-submenu')) {
            this._openSubmenu(item, depth);
        } else {
            this._closeSubmenusFrom(depth);
        }
    },

    _clearAllHovers: function () {
        if (!this._rootEl) return;
        var items = this._rootEl.querySelectorAll('.menu-item');
        for (var i = 0; i < items.length; i++) {
            items[i].classList.remove('hovered', 'active-submenu');
        }
    },

    // ── Item Click ───────────────────────────────────────────────────

    _onItemClick: function (e) {
        var item = e.currentTarget;
        if (item.classList.contains('has-submenu') || item.classList.contains('disabled')) return;

        var actionId = item.getAttribute('data-action');
        if (actionId && this._dotNetRef) {
            e.stopPropagation();
            this._dotNetRef.invokeMethodAsync('OnItemClicked', actionId);
        }
    },

    // ── Event Handlers ───────────────────────────────────────────────

    _onMouseMove: function (e) {
        this._mouse.x = e.clientX;
        this._mouse.y = e.clientY;
    },

    _onClickOutside: function (e) {
        if (!this._rootEl) return;
        if (!this._rootEl.classList.contains('open')) return;
        if (this._rootEl.contains(e.target)) return;

        if (this._dotNetRef) {
            this._dotNetRef.invokeMethodAsync('OnClickOutside');
        }
    },

    _onKeydown: function (e) {
        if (!this._rootEl || !this._rootEl.classList.contains('open')) return;

        switch (e.key) {
            case 'Escape':
                e.preventDefault();
                if (this._openSubmenus.length > 0) {
                    // Close deepest submenu and move focus back
                    var last = this._openSubmenus[this._openSubmenus.length - 1];
                    var parentItem = last.item;
                    this._closeSubmenusFrom(this._openSubmenus.length - 1);
                    parentItem.classList.add('hovered');
                } else if (this._dotNetRef) {
                    this._dotNetRef.invokeMethodAsync('OnEscapePressed');
                }
                break;

            case 'ArrowDown':
                e.preventDefault();
                this._moveFocus(1);
                break;

            case 'ArrowUp':
                e.preventDefault();
                this._moveFocus(-1);
                break;

            case 'ArrowRight':
                e.preventDefault();
                this._enterSubmenu();
                break;

            case 'ArrowLeft':
                e.preventDefault();
                this._exitSubmenu();
                break;

            case 'Home':
                e.preventDefault();
                this._focusFirst();
                break;

            case 'End':
                e.preventDefault();
                this._focusLast();
                break;

            case 'Enter':
            case ' ':
                e.preventDefault();
                this._activateFocused();
                break;
        }
    },

    // ── Keyboard Navigation ──────────────────────────────────────────

    _getActiveMenuList: function () {
        if (this._openSubmenus.length > 0) {
            var lastWrapper = this._openSubmenus[this._openSubmenus.length - 1].wrapper;
            return lastWrapper.querySelector(':scope > .menu-list');
        }
        return this._rootEl ? this._rootEl.querySelector(':scope > .menu-list') : null;
    },

    _getNavigableItems: function (menuList) {
        if (!menuList) return [];
        var items = menuList.querySelectorAll(':scope > .menu-item:not(.disabled)');
        return Array.prototype.slice.call(items);
    },

    _moveFocus: function (direction) {
        var menuList = this._getActiveMenuList();
        var items = this._getNavigableItems(menuList);
        if (items.length === 0) return;

        // Find currently hovered item in this list
        var currentIdx = -1;
        for (var i = 0; i < items.length; i++) {
            if (items[i].classList.contains('hovered')) {
                currentIdx = i;
                break;
            }
        }

        var nextIdx = currentIdx + direction;
        if (nextIdx < 0) nextIdx = items.length - 1;
        if (nextIdx >= items.length) nextIdx = 0;

        // Clear hovers in this list
        for (var j = 0; j < items.length; j++) {
            items[j].classList.remove('hovered');
        }
        items[nextIdx].classList.add('hovered');

        // If new item has no submenu, close any open ones at this depth
        var depth = this._getItemDepth(items[nextIdx]);
        if (!items[nextIdx].classList.contains('has-submenu')) {
            this._closeSubmenusFrom(depth);
        }
    },

    _focusFirst: function () {
        var menuList = this._getActiveMenuList();
        var items = this._getNavigableItems(menuList);
        if (items.length === 0) return;

        for (var j = 0; j < items.length; j++) items[j].classList.remove('hovered');
        items[0].classList.add('hovered');
    },

    _focusLast: function () {
        var menuList = this._getActiveMenuList();
        var items = this._getNavigableItems(menuList);
        if (items.length === 0) return;

        for (var j = 0; j < items.length; j++) items[j].classList.remove('hovered');
        items[items.length - 1].classList.add('hovered');
    },

    _enterSubmenu: function () {
        var menuList = this._getActiveMenuList();
        var items = this._getNavigableItems(menuList);
        var hovered = null;
        for (var i = 0; i < items.length; i++) {
            if (items[i].classList.contains('hovered')) {
                hovered = items[i];
                break;
            }
        }

        if (hovered && hovered.classList.contains('has-submenu')) {
            var depth = this._getItemDepth(hovered);
            this._openSubmenu(hovered, depth);

            // Focus first item in the new submenu
            var newList = this._getActiveMenuList();
            var newItems = this._getNavigableItems(newList);
            if (newItems.length > 0) {
                newItems[0].classList.add('hovered');
            }
        }
    },

    _exitSubmenu: function () {
        if (this._openSubmenus.length === 0) return;

        var last = this._openSubmenus[this._openSubmenus.length - 1];
        var parentItem = last.item;
        this._closeSubmenusFrom(this._openSubmenus.length - 1);

        // Re-highlight the parent item
        var siblings = parentItem.parentElement.querySelectorAll(':scope > .menu-item');
        for (var i = 0; i < siblings.length; i++) siblings[i].classList.remove('hovered');
        parentItem.classList.add('hovered');
    },

    _activateFocused: function () {
        var menuList = this._getActiveMenuList();
        var items = this._getNavigableItems(menuList);
        var hovered = null;
        for (var i = 0; i < items.length; i++) {
            if (items[i].classList.contains('hovered')) {
                hovered = items[i];
                break;
            }
        }

        if (!hovered) return;

        if (hovered.classList.contains('has-submenu')) {
            this._enterSubmenu();
        } else {
            var actionId = hovered.getAttribute('data-action');
            if (actionId && this._dotNetRef) {
                this._dotNetRef.invokeMethodAsync('OnItemClicked', actionId);
            }
        }
    },

    // ── Binding ──────────────────────────────────────────────────────

    _bindMenuItems: function () {
        if (!this._rootEl) return;

        var self = this;
        var items = this._rootEl.querySelectorAll('.menu-item');
        for (var i = 0; i < items.length; i++) {
            var item = items[i];
            item.addEventListener('mouseenter', function (e) { self._onItemMouseEnter(e); });
            item.addEventListener('click', function (e) { self._onItemClick(e); });
        }
    }
};
