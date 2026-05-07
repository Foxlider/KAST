// Drag-to-resize helper for the KAST console side panel.
// Panel is anchored to the right edge; dragging the left handle leftward increases width.
export function start(dotNetRef, initialWidth, minWidth, maxWidth, startX) {
    if (typeof startX !== 'number') {
        startX = (window.event && window.event.clientX) || 0;
    }
    const startWidth = initialWidth;

    const prevCursor = document.body.style.cursor;
    const prevSelect = document.body.style.userSelect;
    document.body.style.cursor = 'ew-resize';
    document.body.style.userSelect = 'none';

    let lastSent = startWidth;
    let rafPending = false;

    function onMove(e) {
        const dx = startX - e.clientX; // drag left -> positive dx -> wider
        let next = startWidth + dx;
        if (next < minWidth) next = minWidth;
        if (next > maxWidth) next = maxWidth;

        if (next === lastSent) return;
        lastSent = next;

        if (rafPending) return;
        rafPending = true;
        requestAnimationFrame(() => {
            rafPending = false;
            dotNetRef.invokeMethodAsync('SetPanelWidth', lastSent);
        });
    }

    function onUp() {
        window.removeEventListener('mousemove', onMove);
        window.removeEventListener('mouseup', onUp);
        document.body.style.cursor = prevCursor;
        document.body.style.userSelect = prevSelect;
    }

    window.addEventListener('mousemove', onMove);
    window.addEventListener('mouseup', onUp);
}
