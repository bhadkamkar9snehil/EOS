// Applies the active EOS palette to every ECharts renderer without coupling
// Razor components to hard-coded colour values.
(() => {
    const calls = new Map();

    const palette = () => window.epaTheme?.chartPalette?.() || {
        series: ['#2563EB', '#0891B2', '#15803D', '#7C3AED', '#B45309', '#0369A1', '#B91C1C', '#DB2777'],
        operational: '#2563EB',
        timesheet: '#0891B2',
        approval: '#7C3AED',
        attendance: '#15803D',
        billable: '#2563EB',
        nonBillable: '#B45309',
        training: '#7C3AED',
        office: '#15803D',
        good: '#15803D',
        warning: '#B45309',
        serious: '#C2410C',
        critical: '#B91C1C',
        grid: '#E5E7EB',
        axis: '#CBD5E1',
        muted: '#64748B',
        ink: '#0F172A',
        tooltip: '#0F172A',
        tooltipText: '#F8FAFC',
        heatLow: '#EFF6FF',
        heatHigh: '#1D4ED8',
        surface: '#FFFFFF'
    };

    function colorFor(name, index, p) {
        const value = String(name || '').toLowerCase();
        if (value.includes('operational') || value.includes('team score') || value.includes('forecast')) return p.operational;
        if (value.includes('timesheet')) return p.timesheet;
        if (value.includes('approval')) return p.approval;
        if (value.includes('attendance')) return p.attendance;
        if (value.includes('non-bill')) return p.nonBillable;
        if (value.includes('billable')) return p.billable;
        if (value.includes('training')) return p.training;
        if (value.includes('office')) return p.office;
        if (value.includes('optimal') || value.includes('good')) return p.good;
        if (value.includes('warning') || value.includes('high workload')) return p.warning;
        if (value.includes('serious')) return p.serious;
        if (value.includes('critical')) return p.critical;
        return p.series[index % p.series.length];
    }

    function restyle(id) {
        const chart = window.epaCharts?.ensure?.(id);
        if (!chart || chart.isDisposed?.()) return;

        const p = palette();
        const current = chart.getOption();
        const patch = {
            color: p.series,
            textStyle: { color: p.ink },
            tooltip: {
                backgroundColor: p.tooltip,
                borderWidth: 0,
                textStyle: { color: p.tooltipText }
            }
        };

        if (current.legend?.length) {
            patch.legend = current.legend.map(() => ({
                textStyle: { color: p.ink }
            }));
        }

        if (current.xAxis?.length) {
            patch.xAxis = current.xAxis.map(() => ({
                axisLine: { lineStyle: { color: p.axis } },
                axisLabel: { color: p.muted },
                nameTextStyle: { color: p.muted },
                splitLine: { lineStyle: { color: p.grid } }
            }));
        }

        if (current.yAxis?.length) {
            patch.yAxis = current.yAxis.map(() => ({
                axisLine: { lineStyle: { color: p.axis } },
                axisLabel: { color: p.muted },
                nameTextStyle: { color: p.muted },
                splitLine: { lineStyle: { color: p.grid } }
            }));
        }

        if (current.radar?.length) {
            patch.radar = current.radar.map(() => ({
                axisName: { color: p.ink },
                splitLine: { lineStyle: { color: p.grid } },
                axisLine: { lineStyle: { color: p.grid } }
            }));
        }

        if (current.visualMap?.length) {
            patch.visualMap = current.visualMap.map(() => ({
                inRange: {
                    color: [p.heatLow, p.series[5], p.series[0], p.heatHigh]
                }
            }));
        }

        if (current.series?.length) {
            patch.series = current.series.map((series, index) => {
                const color = colorFor(series.name, index, p);
                const update = {};

                if (series.type === 'line') {
                    update.lineStyle = { color };
                    update.itemStyle = { color };
                } else if (series.type === 'bar' && series.name) {
                    update.itemStyle = { color };
                    if (series.label?.show) update.label = { color: p.ink };
                } else if (series.type === 'bar') {
                    if (series.label?.show) update.label = { color: p.ink };
                } else if (series.type === 'scatter') {
                    update.itemStyle = {
                        color,
                        borderColor: p.surface
                    };
                } else if (series.type === 'radar' && Array.isArray(series.data)) {
                    update.data = series.data.map((item, itemIndex) => {
                        const itemColor = colorFor(item.name, itemIndex, p);
                        return {
                            ...item,
                            lineStyle: { ...(item.lineStyle || {}), color: itemColor },
                            areaStyle: { ...(item.areaStyle || {}), color: itemColor },
                            itemStyle: { ...(item.itemStyle || {}), color: itemColor }
                        };
                    });
                } else if (series.type === 'graph') {
                    update.lineStyle = { color: p.axis };
                    update.emphasis = { lineStyle: { color: p.operational } };
                }

                return update;
            });
        }

        chart.setOption(patch);
    }

    function wrap(target, methodName) {
        const original = target?.[methodName];
        if (typeof original !== 'function') return;

        target[methodName] = function (...args) {
            const id = args[0];
            const render = () => {
                original.apply(target, args);
                restyle(id);
            };
            calls.set(`${methodName}:${id}`, render);
            render();
        };
    }

    function refreshTheme() {
        for (const render of calls.values()) {
            try { render(); } catch { }
        }
    }

    const charts = window.epaCharts;
    [
        'gauge',
        'radar',
        'scatter',
        'trend',
        'multiline',
        'heatmap',
        'network',
        'bars',
        'verticalBars'
    ].forEach(name => wrap(charts, name));

    const analytics = window.epaAnalyticsCharts;
    wrap(analytics, 'weightedStack');

    if (charts) charts.refreshTheme = refreshTheme;
    if (analytics) analytics.refreshTheme = refreshTheme;

    window.addEventListener('epa-theme-changed', refreshTheme);
})();
