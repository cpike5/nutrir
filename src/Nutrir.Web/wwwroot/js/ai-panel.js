window.aiPanel = {
    _storageKey: 'nutrir_ai_panel_open',
    _wideKey: 'nutrir_ai_panel_wide',
    _dotNetRef: null,
    _debounceTimer: null,
    _currentValue: '',
    _boundHandleInput: null,
    _boundHandleKeyDown: null,
    _boundHandlePaste: null,
    _activeMentionTrigger: null,

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

    _getInputElement: function () {
        return document.getElementById('ai-input');
    },

    _getPlainText: function () {
        var input = this._getInputElement();
        if (!input) return '';
        return input.innerText || '';
    },

    initInputHandler: function (dotNetRef) {
        this._dotNetRef = dotNetRef;
        this._currentValue = '';
        this._activeMentionTrigger = null;
        var self = this;

        this._boundHandleInput = function () {
            self._currentValue = self._getPlainText();

            if (self._debounceTimer) {
                clearTimeout(self._debounceTimer);
            }
            self._debounceTimer = setTimeout(function () {
                if (self._dotNetRef) {
                    self._dotNetRef.invokeMethodAsync('OnInputTextChanged', self._currentValue);
                }
            }, 200);

            self._detectMentionTrigger();
        };

        this._boundHandleKeyDown = function (e) {
            // Handle backspace into mention tags
            if (e.key === 'Backspace') {
                var sel = window.getSelection();
                if (sel && sel.rangeCount > 0 && sel.isCollapsed) {
                    var range = sel.getRangeAt(0);
                    var node = range.startContainer;
                    var offset = range.startOffset;

                    // Check if cursor is at the start of a text node right after a tag
                    if (node.nodeType === Node.TEXT_NODE && offset === 0) {
                        var prev = node.previousSibling;
                        if (prev && prev.nodeType === Node.ELEMENT_NODE && prev.classList.contains('cc-mention-tag')) {
                            e.preventDefault();
                            prev.parentNode.removeChild(prev);
                            self._currentValue = self._getPlainText();
                            if (self._dotNetRef) {
                                self._dotNetRef.invokeMethodAsync('OnInputTextChanged', self._currentValue);
                            }
                            return;
                        }
                    }

                    // Check if cursor is at offset in element node, and previous child is a tag
                    if (node.nodeType === Node.ELEMENT_NODE && offset > 0) {
                        var prevChild = node.childNodes[offset - 1];
                        if (prevChild && prevChild.nodeType === Node.ELEMENT_NODE && prevChild.classList.contains('cc-mention-tag')) {
                            e.preventDefault();
                            prevChild.parentNode.removeChild(prevChild);
                            self._currentValue = self._getPlainText();
                            if (self._dotNetRef) {
                                self._dotNetRef.invokeMethodAsync('OnInputTextChanged', self._currentValue);
                            }
                            return;
                        }
                    }
                }
            }

            var actionKeys = ['Enter', 'ArrowDown', 'ArrowUp', 'Tab', 'Escape'];
            if (actionKeys.indexOf(e.key) === -1) return;

            // For Enter, flush the current value immediately
            if (e.key === 'Enter') {
                if (self._debounceTimer) {
                    clearTimeout(self._debounceTimer);
                    self._debounceTimer = null;
                }
                self._currentValue = self._getPlainText();
            }

            self._dotNetRef.invokeMethodAsync('OnInputKeyAction', e.key);

            // Prevent default browser behavior for action keys
            if (e.key === 'Tab' || e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === 'Enter') {
                e.preventDefault();
            }
        };

        this._boundHandlePaste = function (e) {
            e.preventDefault();
            var text = (e.clipboardData || window.clipboardData).getData('text/plain');
            document.execCommand('insertText', false, text);
        };

        var input = this._getInputElement();
        if (input) {
            input.addEventListener('input', this._boundHandleInput);
            input.addEventListener('keydown', this._boundHandleKeyDown);
            input.addEventListener('paste', this._boundHandlePaste);
        }
    },

    _detectMentionTrigger: function () {
        var input = this._getInputElement();
        if (!input || !this._dotNetRef) return;

        var sel = window.getSelection();
        if (!sel || sel.rangeCount === 0 || !sel.isCollapsed) {
            this._dismissMention();
            return;
        }

        var range = sel.getRangeAt(0);
        var node = range.startContainer;

        // Only work with text nodes inside the input
        if (node.nodeType !== Node.TEXT_NODE || !input.contains(node)) {
            this._dismissMention();
            return;
        }

        var textBeforeCursor = node.textContent.substring(0, range.startOffset);

        // Walk backward to find @ or # trigger
        var triggerIndex = -1;
        var triggerChar = null;

        for (var i = textBeforeCursor.length - 1; i >= 0; i--) {
            var ch = textBeforeCursor[i];

            // If we hit a space before finding a trigger, no active mention
            if (ch === ' ' || ch === '\n') {
                break;
            }

            if (ch === '@' || ch === '#') {
                // Valid trigger: must be at position 0 or preceded by a space
                if (i === 0 || textBeforeCursor[i - 1] === ' ' || textBeforeCursor[i - 1] === '\n') {
                    triggerIndex = i;
                    triggerChar = ch;
                }
                break;
            }
        }

        if (triggerChar !== null && triggerIndex >= 0) {
            var query = textBeforeCursor.substring(triggerIndex + 1);
            this._activeMentionTrigger = triggerChar;
            this._dotNetRef.invokeMethodAsync('OnMentionTrigger', triggerChar, query);
        } else {
            this._dismissMention();
        }
    },

    _dismissMention: function () {
        if (this._activeMentionTrigger !== null) {
            this._activeMentionTrigger = null;
            if (this._dotNetRef) {
                this._dotNetRef.invokeMethodAsync('OnMentionDismiss');
            }
        }
    },

    insertMentionTag: function (entityType, entityId, displayName, triggerChar) {
        var input = this._getInputElement();
        if (!input) return;

        var sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return;

        var range = sel.getRangeAt(0);
        var node = range.startContainer;

        if (node.nodeType !== Node.TEXT_NODE || !input.contains(node)) return;

        var textBeforeCursor = node.textContent.substring(0, range.startOffset);
        var textAfterCursor = node.textContent.substring(range.startOffset);

        // Find the trigger character position walking backward
        var triggerIndex = -1;
        for (var i = textBeforeCursor.length - 1; i >= 0; i--) {
            if (textBeforeCursor[i] === triggerChar) {
                if (i === 0 || textBeforeCursor[i - 1] === ' ' || textBeforeCursor[i - 1] === '\n') {
                    triggerIndex = i;
                }
                break;
            }
        }

        if (triggerIndex < 0) return;

        var textBefore = textBeforeCursor.substring(0, triggerIndex);

        // Create the mention span
        var span = document.createElement('span');
        span.className = 'cc-mention-tag';
        span.setAttribute('data-entity-type', entityType);
        span.setAttribute('data-entity-id', entityId);
        span.contentEditable = 'false';
        span.textContent = triggerChar + displayName;

        // Create a nbsp text node for cursor positioning
        var nbsp = document.createTextNode('\u00A0');

        // Replace the text node: split into before-trigger text, span, nbsp, after-cursor text
        var parent = node.parentNode;

        // Create the after text node (may be empty)
        var afterNode = document.createTextNode(textAfterCursor);

        if (textBefore.length > 0) {
            // Set existing text node to the "before" text
            node.textContent = textBefore;
            // Insert span, nbsp, and after text after the current node
            parent.insertBefore(span, node.nextSibling);
            parent.insertBefore(nbsp, span.nextSibling);
            parent.insertBefore(afterNode, nbsp.nextSibling);
        } else {
            // No text before trigger, replace the node entirely
            parent.insertBefore(span, node);
            parent.insertBefore(nbsp, span.nextSibling);
            parent.insertBefore(afterNode, nbsp.nextSibling);
            parent.removeChild(node);
        }

        // Place cursor after the nbsp
        var newRange = document.createRange();
        newRange.setStartAfter(nbsp);
        newRange.collapse(true);
        sel.removeAllRanges();
        sel.addRange(newRange);

        // Update current value and notify .NET
        this._activeMentionTrigger = null;
        this._currentValue = this._getPlainText();
        if (this._dotNetRef) {
            this._dotNetRef.invokeMethodAsync('OnMentionDismiss');
            this._dotNetRef.invokeMethodAsync('OnInputTextChanged', this._currentValue);
        }
    },

    getCurrentInputValue: function () {
        return this._getPlainText();
    },

    setInputValue: function (value) {
        var input = this._getInputElement();
        if (input) {
            input.textContent = value;
        }
        this._currentValue = value;
    },

    flushInput: function () {
        if (this._debounceTimer) {
            clearTimeout(this._debounceTimer);
            this._debounceTimer = null;
        }
        this._currentValue = this._getPlainText();
        return this._currentValue;
    },

    getPlainTextWithTags: function () {
        var input = this._getInputElement();
        if (!input) return '';

        var result = '';
        var childNodes = input.childNodes;

        for (var i = 0; i < childNodes.length; i++) {
            var child = childNodes[i];
            if (child.nodeType === Node.TEXT_NODE) {
                result += child.textContent;
            } else if (child.nodeType === Node.ELEMENT_NODE) {
                if (child.classList.contains('cc-mention-tag')) {
                    var entType = child.getAttribute('data-entity-type');
                    var entId = child.getAttribute('data-entity-id');
                    var display = child.textContent;
                    // Strip the trigger char from display name for the tag encoding
                    if (display.length > 0 && (display[0] === '@' || display[0] === '#')) {
                        display = display.substring(1);
                    }
                    result += '{{' + entType + ':' + entId + ':' + display + '}}';
                } else {
                    // For other elements (e.g., <br>), get their text content
                    result += child.textContent || '';
                }
            }
        }

        return result;
    },

    clearInput: function () {
        var input = this._getInputElement();
        if (input) {
            input.innerHTML = '';
        }
        this._currentValue = '';
        this._activeMentionTrigger = null;
    },

    getMentionDropdownPosition: function () {
        var sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) {
            return { bottom: 0, left: 0 };
        }

        var range = sel.getRangeAt(0).cloneRange();
        range.collapse(true);

        // Use a temporary span to get position since collapsed ranges
        // may return all zeros for getBoundingClientRect
        var marker = document.createElement('span');
        marker.textContent = '\u200B'; // zero-width space
        range.insertNode(marker);

        var rect = marker.getBoundingClientRect();

        // Get position relative to the panel
        var panel = this._getInputElement();
        var panelRect = panel ? panel.closest('.cc-ai-panel, .cc-ai-chat-panel')?.getBoundingClientRect() : null;

        var inputArea = panel ? panel.closest('.cc-ai-input-area') : null;
        var inputAreaRect = inputArea ? inputArea.getBoundingClientRect() : null;

        // Calculate bottom-relative position (dropdown appears above the cursor)
        var bottom = 0;
        var left = rect.left;

        if (inputAreaRect) {
            bottom = inputAreaRect.bottom - rect.top + 4; // 4px gap above cursor
            left = rect.left - inputAreaRect.left;
        }

        // Clean up the marker
        marker.parentNode.removeChild(marker);

        // Normalize the DOM after marker removal to merge adjacent text nodes
        if (panel) {
            panel.normalize();
        }

        return { bottom: bottom, left: left };
    },

    disposeInputHandler: function () {
        var input = this._getInputElement();
        if (input) {
            if (this._boundHandleInput) {
                input.removeEventListener('input', this._boundHandleInput);
            }
            if (this._boundHandleKeyDown) {
                input.removeEventListener('keydown', this._boundHandleKeyDown);
            }
            if (this._boundHandlePaste) {
                input.removeEventListener('paste', this._boundHandlePaste);
            }
        }
        if (this._debounceTimer) {
            clearTimeout(this._debounceTimer);
        }
        this._dotNetRef = null;
        this._currentValue = '';
        this._boundHandleInput = null;
        this._boundHandleKeyDown = null;
        this._boundHandlePaste = null;
        this._activeMentionTrigger = null;
    }
};
