// Admin reporting charts.
//
// The numbers are fetched from /Report/Data rather than baked into the page, so
// the same figures can be inspected on their own and the view stays free of
// embedded data. That fetch doubles as the AJAX part of this feature.
(function () {
    'use strict';

    if (typeof Chart === 'undefined') return;

    // One palette for the whole dashboard so the charts read as a set.
    var INK = '#16262e';
    var MUTED = '#5a7280';
    var LINE = '#dae4e8';
    var BRAND = '#0e7490';
    // Teal through honey, the two ends of the SharkBee palette, with enough
    // lightness separation to stay distinguishable in the doughnut.
    var SERIES = ['#0e7490', '#e0a422', '#2f7d52', '#4a6fa5', '#9c5673',
                  '#43a2a8', '#b3762a', '#6b7f3f'];

    Chart.defaults.font.family = '"Plus Jakarta Sans", "Segoe UI", system-ui, sans-serif';
    Chart.defaults.color = MUTED;

    // Draws the percentage share onto each slice of a pie or doughnut. Written
    // by hand instead of adding chartjs-plugin-datalabels, so this stays our
    // own code.
    var sliceCount = function (values) {
        var total = 0;
        for (var i = 0; i < values.length; i++) total += Number(values[i]);
        return total;
    };

    var percentLabels = {
        id: 'percentLabels',
        afterDatasetsDraw: function (chart) {
            var values = chart.data.datasets[0].data;
            var total = sliceCount(values);
            if (!total) return;

            var cx = chart.ctx;
            cx.save();
            cx.font = '600 12px ' + Chart.defaults.font.family;
            cx.fillStyle = '#ffffff';
            cx.textAlign = 'center';
            cx.textBaseline = 'middle';

            chart.getDatasetMeta(0).data.forEach(function (arc, i) {
                var share = Number(values[i]) / total * 100;

                // A thinner slice cannot hold a readable label, so it is left
                // to the legend and the tooltip.
                if (share < 5) return;

                var at = arc.tooltipPosition();
                cx.fillText(share.toFixed(1) + '%', at.x, at.y);
            });

            cx.restore();
        }
    };

    var money = function (v) { return 'RM ' + Number(v).toLocaleString('en-MY', { minimumFractionDigits: 2, maximumFractionDigits: 2 }); };

    fetch('/Report/Data', { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
        .then(function (r) { return r.json(); })
        .then(draw)
        .catch(function () {
            var boxes = document.querySelectorAll('.charts canvas');
            for (var i = 0; i < boxes.length; i++) {
                boxes[i].insertAdjacentHTML('afterend',
                    '<p class="empty">Could not load chart data.</p>');
            }
        });

    function gridOptions(extra) {
        var base = {
            responsive: true,
            maintainAspectRatio: false,
            plugins: { legend: { display: false } },
            scales: {
                x: { grid: { display: false }, border: { color: LINE } },
                y: { beginAtZero: true, grid: { color: LINE }, border: { display: false } }
            }
        };
        return Object.assign(base, extra || {});
    }

    function draw(d) {
        // Monthly revenue
        new Chart(document.getElementById('revenueChart'), {
            type: 'bar',
            data: {
                labels: d.monthlyRevenue.labels,
                datasets: [{
                    data: d.monthlyRevenue.values,
                    backgroundColor: BRAND,
                    borderRadius: 4,
                    maxBarThickness: 38
                }]
            },
            options: gridOptions({
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            label: function (c) { return money(c.parsed.y); }
                        }
                    }
                },
                scales: {
                    x: { grid: { display: false }, border: { color: LINE } },
                    y: {
                        beginAtZero: true,
                        grid: { color: LINE },
                        border: { display: false },
                        ticks: { callback: function (v) { return 'RM ' + v; } }
                    }
                }
            })
        });

        // Most popular services
        new Chart(document.getElementById('servicesChart'), {
            type: 'doughnut',
            data: {
                labels: d.popularServices.labels,
                datasets: [{
                    data: d.popularServices.values,
                    backgroundColor: SERIES,
                    borderColor: '#ffffff',
                    borderWidth: 2
                }]
            },
            plugins: [percentLabels],
            options: {
                responsive: true,
                maintainAspectRatio: false,
                cutout: '58%',
                plugins: {
                    legend: {
                        position: 'right',
                        labels: {
                            boxWidth: 12,
                            padding: 10,
                            generateLabels: function (chart) {
                                var values = chart.data.datasets[0].data;
                                var total = sliceCount(values);

                                return chart.data.labels.map(function (label, i) {
                                    var share = total ? Number(values[i]) / total * 100 : 0;

                                    return {
                                        text: label + ' — ' + share.toFixed(1) + '%',
                                        fillStyle: SERIES[i % SERIES.length],
                                        strokeStyle: '#ffffff',
                                        lineWidth: 2,
                                        index: i
                                    };
                                });
                            }
                        }
                    },
                    tooltip: {
                        callbacks: {
                            label: function (c) {
                                var total = sliceCount(c.dataset.data);
                                var share = total ? c.parsed / total * 100 : 0;
                                var noun = c.parsed === 1 ? ' booking' : ' bookings';

                                return c.label + ': ' + c.parsed + noun +
                                       ' (' + share.toFixed(1) + '%)';
                            }
                        }
                    }
                }
            }
        });

        // Groomer utilisation
        new Chart(document.getElementById('utilisationChart'), {
            type: 'bar',
            data: {
                labels: d.groomerUtilisation.labels,
                datasets: [{
                    data: d.groomerUtilisation.values,
                    backgroundColor: '#2f7d52',
                    borderRadius: 4,
                    maxBarThickness: 42
                }]
            },
            options: gridOptions({
                indexAxis: 'y',
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: { label: function (c) { return c.parsed.x + '% booked'; } }
                    }
                },
                scales: {
                    x: {
                        beginAtZero: true, max: 100,
                        grid: { color: LINE }, border: { display: false },
                        ticks: { callback: function (v) { return v + '%'; } }
                    },
                    y: { grid: { display: false }, border: { color: LINE } }
                }
            })
        });

        // Busiest hours
        new Chart(document.getElementById('hoursChart'), {
            type: 'line',
            data: {
                labels: d.peakHours.labels,
                datasets: [{
                    data: d.peakHours.values,
                    borderColor: BRAND,
                    backgroundColor: 'rgba(181, 98, 47, 0.12)',
                    fill: true,
                    tension: 0.32,
                    pointRadius: 3,
                    pointBackgroundColor: BRAND
                }]
            },
            options: gridOptions()
        });
    }
})();
