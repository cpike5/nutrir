window.reportsChart = {
    _instances: {},

    create: function (canvasId, labels, visitData, noShowData, cancellationData) {
        this.destroy(canvasId);

        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        const ctx = canvas.getContext('2d');

        const styles = getComputedStyle(document.documentElement);
        const primary = styles.getPropertyValue('--color-primary').trim() || '#7c3aed';
        const warning = styles.getPropertyValue('--color-warning').trim() || '#f59e0b';
        const danger = styles.getPropertyValue('--color-danger').trim() || '#ef4444';
        const textColor = styles.getPropertyValue('--color-text-muted').trim() || '#6b7280';
        const borderColor = styles.getPropertyValue('--color-border').trim() || '#e5e7eb';

        this._instances[canvasId] = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [
                    {
                        label: 'Visits',
                        data: visitData,
                        backgroundColor: primary + 'cc',
                        borderColor: primary,
                        borderWidth: 1,
                        borderRadius: 3
                    },
                    {
                        label: 'No-Shows',
                        data: noShowData,
                        backgroundColor: warning + 'cc',
                        borderColor: warning,
                        borderWidth: 1,
                        borderRadius: 3
                    },
                    {
                        label: 'Cancellations',
                        data: cancellationData,
                        backgroundColor: danger + 'cc',
                        borderColor: danger,
                        borderWidth: 1,
                        borderRadius: 3
                    }
                ]
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
                        beginAtZero: true,
                        ticks: {
                            color: textColor,
                            font: { size: 11 },
                            stepSize: 1
                        },
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
