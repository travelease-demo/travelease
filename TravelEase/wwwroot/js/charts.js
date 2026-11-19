/* Charts for the Dashboard (monthly revenue) and Reports (monthly bookings by status). */
(function () {
    "use strict";
    if (!window.Chart) return;

    const css = getComputedStyle(document.documentElement);
    const token = (name, fallback) => (css.getPropertyValue(name) || fallback).trim();
    const currency = document.body.dataset.currency || "";
    const number = new Intl.NumberFormat("en-IN", { maximumFractionDigits: 0 });

    const colors = {
        primary: token("--te-primary", "#2a78d6"),
        confirmed: token("--te-confirmed", "#1a9a5a"),
        pending: token("--te-pending-fill", "#eda100"),
        cancelled: token("--te-cancelled-fill", "#e34948"),
        grid: "#eef1f6",
        text: token("--te-muted", "#8a94a6")
    };

    Chart.defaults.font.family = token("--te-font", "Inter, system-ui, sans-serif");
    Chart.defaults.font.size = 12;
    Chart.defaults.color = colors.text;

    const tooltip = {
        backgroundColor: "#0f172a",
        padding: 10,
        cornerRadius: 8,
        titleFont: { weight: "600" },
        displayColors: true,
        boxPadding: 4
    };

    function compact(v) {
        if (v >= 10000000) return `${currency}${(v / 10000000).toFixed(1)}Cr`;
        if (v >= 100000) return `${currency}${(v / 100000).toFixed(1)}L`;
        if (v >= 1000) return `${currency}${(v / 1000).toFixed(0)}k`;
        return `${currency}${v}`;
    }

    function readJson(id) {
        const el = document.getElementById(id);
        return el ? JSON.parse(el.textContent) : null;
    }

    // ----- Dashboard: monthly revenue (single series, so no legend) -----
    const dashboard = readJson("dashboard-data");
    const revenueCanvas = document.getElementById("revenueChart");
    if (dashboard && revenueCanvas) {
        new Chart(revenueCanvas, {
            type: "bar",
            data: {
                labels: dashboard.labels,
                datasets: [{
                    label: "Revenue",
                    data: dashboard.revenue,
                    backgroundColor: colors.primary,
                    hoverBackgroundColor: "#1f63b8",
                    borderRadius: { topLeft: 4, topRight: 4 },
                    borderSkipped: "bottom",
                    maxBarThickness: 34
                }]
            },
            options: {
                maintainAspectRatio: false,
                interaction: { mode: "index", intersect: false },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        ...tooltip,
                        callbacks: {
                            label: (ctx) => ` Revenue: ${currency}${number.format(ctx.parsed.y)}`,
                            afterLabel: (ctx) => ` Confirmed bookings: ${dashboard.bookings[ctx.dataIndex]}`
                        }
                    }
                },
                scales: {
                    x: { grid: { display: false }, border: { display: false } },
                    y: { beginAtZero: true, grid: { color: colors.grid }, border: { display: false }, ticks: { callback: compact, maxTicksLimit: 6 } }
                }
            }
        });
    }

    // ----- Reports: monthly bookings by status (stacked) -----
    const monthly = readJson("monthly-data");
    const monthlyCanvas = document.getElementById("monthlyChart");
    if (monthly && monthlyCanvas) {
        const series = (label, data, color) => ({
            label, data, backgroundColor: color,
            borderColor: "#ffffff", borderWidth: { top: 2 }, borderSkipped: "bottom",
            maxBarThickness: 34
        });
        new Chart(monthlyCanvas, {
            type: "bar",
            data: {
                labels: monthly.labels,
                datasets: [
                    series("Confirmed", monthly.confirmed, colors.confirmed),
                    series("Pending", monthly.pending, colors.pending),
                    series("Cancelled", monthly.cancelled, colors.cancelled)
                ]
            },
            options: {
                maintainAspectRatio: false,
                interaction: { mode: "index", intersect: false },
                plugins: {
                    legend: { display: false }, // HTML legend is rendered above the chart
                    tooltip: { ...tooltip, callbacks: { footer: (items) => `Total: ${items.reduce((s, i) => s + i.parsed.y, 0)} bookings` } }
                },
                scales: {
                    x: { stacked: true, grid: { display: false }, border: { display: false } },
                    y: { stacked: true, beginAtZero: true, grid: { color: colors.grid }, border: { display: false }, ticks: { precision: 0, maxTicksLimit: 6 } }
                }
            }
        });
    }
})();
