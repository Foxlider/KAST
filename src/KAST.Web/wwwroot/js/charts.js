// Chart.js interop for KAST monitoring graphs
const _charts = {};

window.kastCharts = {
    create(canvasId, config) {
        const ctx = document.getElementById(canvasId);
        if (!ctx) return;
        if (_charts[canvasId]) {
            _charts[canvasId].destroy();
        }
        _charts[canvasId] = new Chart(ctx, config);
    },

    update(canvasId, labels, datasets) {
        const chart = _charts[canvasId];
        if (!chart) return;
        chart.data.labels = labels;
        for (let i = 0; i < datasets.length; i++) {
            if (chart.data.datasets[i]) {
                chart.data.datasets[i].data = datasets[i];
            }
        }
        chart.update('none'); // skip animation for real-time feel
    },

    destroy(canvasId) {
        if (_charts[canvasId]) {
            _charts[canvasId].destroy();
            delete _charts[canvasId];
        }
    }
};
