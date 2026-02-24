window.progressChart = {
    _instances: {},

    create: function (canvasId, labels, values, label) {
        this.destroy(canvasId);

        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        const ctx = canvas.getContext('2d');

        // Try to read CSS custom properties for theming
        const styles = getComputedStyle(document.documentElement);
        const primary = styles.getPropertyValue('--color-primary').trim() || '#7c3aed';
        const primaryMuted = styles.getPropertyValue('--color-primary-muted').trim() || 'rgba(124, 58, 237, 0.1)';
        const textColor = styles.getPropertyValue('--color-text-muted').trim() || '#6b7280';
        const borderColor = styles.getPropertyValue('--color-border').trim() || '#e5e7eb';

        this._instances[canvasId] = new Chart(ctx, {
            type: 'line',
            data: {
                labels: labels,
                datasets: [{
                    label: label,
                    data: values,
                    borderColor: primary,
                    backgroundColor: primaryMuted,
                    borderWidth: 2,
                    pointRadius: 4,
                    pointHoverRadius: 6,
                    pointBackgroundColor: primary,
                    fill: true,
                    tension: 0.3
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        display: true,
                        position: 'top',
                        labels: {
                            color: textColor,
                            font: { size: 12 },
                            boxWidth: 12
                        }
                    },
                    tooltip: {
                        mode: 'index',
                        intersect: false
                    }
                },
                scales: {
                    x: {
                        ticks: { color: textColor, font: { size: 11 } },
                        grid: { color: borderColor }
                    },
                    y: {
                        ticks: { color: textColor, font: { size: 11 } },
                        grid: { color: borderColor }
                    }
                },
                interaction: {
                    mode: 'nearest',
                    axis: 'x',
                    intersect: false
                }
            }
        });
    },

    destroy: function (canvasId) {
        if (this._instances[canvasId]) {
            this._instances[canvasId].destroy();
            delete this._instances[canvasId];
        }
    }
};
