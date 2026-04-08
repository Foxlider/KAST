// Drag-and-drop interop for mod columns
// Uses a global variable to pass drag data since Blazor's ondragstart
// doesn't allow setting dataTransfer. The drop zones call back to .NET.

// Utility: scroll an element ref to the bottom
window.kastScrollToBottom = function (el) {
    if (el) el.scrollTop = el.scrollHeight;
};

window.kastDragDrop = {
    _dragModId: null,
    _dragSource: null,

    startDrag: function (modId, source) {
        window.kastDragDrop._dragModId = modId;
        window.kastDragDrop._dragSource = source;
    },

    initDropZone: function (element, dotNetRef, targetColumn) {
        if (!element || element._kastInitialized) return;
        element._kastInitialized = true;

        element.addEventListener('dragover', function (e) {
            e.preventDefault();
            e.dataTransfer.dropEffect = 'move';
            element.classList.add('kast-drop-hover');
        });

        element.addEventListener('dragleave', function (e) {
            if (!element.contains(e.relatedTarget)) {
                element.classList.remove('kast-drop-hover');
            }
        });

        element.addEventListener('drop', function (e) {
            e.preventDefault();
            element.classList.remove('kast-drop-hover');
            var modId = window.kastDragDrop._dragModId;
            var source = window.kastDragDrop._dragSource;
            if (modId !== null) {
                dotNetRef.invokeMethodAsync('OnModDropped', modId, source, targetColumn);
            }
            window.kastDragDrop._dragModId = null;
            window.kastDragDrop._dragSource = null;
        });
    }
};
