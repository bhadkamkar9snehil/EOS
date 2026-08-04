// Thin ECharts wrapper called from Blazor via IJSRuntime. One chart instance is
// kept per container id so a re-render updates in place (and animates) instead
// of tearing the chart down every refresh.
(function () {
    const PALETTE = {
        blue: '#2a78d6', orange: '#eb6834', aqua: '#1baf7a', violet: '#4a3aa7',
        good: '#0ca30c', warning: '#fab219', serious: '#ec835a', critical: '#d03b3b',
        grid: '#e1e0d9', axis: '#c3c2b7', muted: '#898781', ink: '#0b0b0b'
    };
    const BAND_COLOR = { good: PALETTE.good, warning: PALETTE.warning, serious: PALETTE.serious, critical: PALETTE.critical };

    const instances = new Map();
    const observers = new Map();

    // Bridge back into Blazor: Overview registers itself once via registerDrilldown,
    // and any chart click that names an employee routes through here.
    let drilldownRef = null;
    function registerDrilldown(ref) { drilldownRef = ref; }
    function drill(name) {
        if (!drilldownRef || !name) return;
        // The active page's reference can be mid-swap right after navigation; a stale
        // or disposed target should be a no-op, not an unhandled rejection in console.
        drilldownRef.invokeMethodAsync('DrilldownTo', name).catch(() => {});
    }

    function ensure(id) {
        let chart = instances.get(id);
        if (chart && !chart.isDisposed()) return chart;
        const el = document.getElementById(id);
        if (!el) return null;
        chart = echarts.init(el, null, { renderer: 'svg' });
        instances.set(id, chart);
        const ro = new ResizeObserver(() => chart.resize());
        ro.observe(el);
        observers.set(id, ro);
        return chart;
    }

    function dispose(id) {
        const chart = instances.get(id);
        if (chart && !chart.isDisposed()) chart.dispose();
        instances.delete(id);
        const ro = observers.get(id);
        if (ro) ro.disconnect();
        observers.delete(id);
    }

    const baseAnimation = { animationDuration: 650, animationEasing: 'cubicOut', animationDurationUpdate: 400 };

    function gauge(id, value, band) {
        const chart = ensure(id); if (!chart) return;
        const color = BAND_COLOR[band] || PALETTE.blue;
        chart.setOption({
            ...baseAnimation,
            series: [{
                type: 'gauge', startAngle: 210, endAngle: -30, min: 0, max: 100,
                radius: '92%',
                progress: { show: true, width: 14, itemStyle: { color } },
                axisLine: { lineStyle: { width: 14, color: [[1, '#eef1f6']] } },
                axisTick: { show: false }, splitLine: { show: false }, axisLabel: { show: false },
                pointer: { show: false },
                anchor: { show: false },
                detail: {
                    valueAnimation: true, fontSize: 30, fontWeight: 600, color: PALETTE.ink,
                    offsetCenter: [0, '10%'], formatter: (v) => v.toFixed(1)
                },
                title: { show: false },
                data: [{ value }]
            }]
        });
    }

    function radar(id, indicatorNames, maxValue, series) {
        const chart = ensure(id); if (!chart) return;
        chart.setOption({
            ...baseAnimation,
            tooltip: {
                trigger: 'item',
                formatter: (p) => {
                    const rows = indicatorNames.map((n, i) => `${n}: <b>${p.value[i].toFixed(0)}</b>`).join('<br/>');
                    return `<strong>${p.name}</strong><br/>${rows}`;
                }
            },
            legend: { show: false },
            radar: {
                indicator: indicatorNames.map(n => ({ name: n, max: maxValue })),
                radius: '68%',
                axisName: { color: '#52514e', fontSize: 11 },
                splitArea: { areaStyle: { color: ['transparent'] } },
                splitLine: { lineStyle: { color: PALETTE.grid } },
                axisLine: { lineStyle: { color: PALETTE.grid } }
            },
            series: [{
                type: 'radar',
                data: series.map(s => ({
                    name: s.name, value: s.values,
                    lineStyle: { color: s.color, width: 2 },
                    areaStyle: { color: s.color, opacity: 0.12 },
                    itemStyle: { color: s.color }
                }))
            }]
        });
    }

    function scatter(id, points, ceilingX, targetY) {
        const chart = ensure(id); if (!chart) return;
        const byBand = { optimal: [], high: [], under: [] };
        for (const p of points) (byBand[p.band] || byBand.under).push([p.x, p.y, p.name, p.tooltip]);
        const colorOf = { optimal: PALETTE.aqua, high: PALETTE.orange, under: PALETTE.blue };
        const nameOf = { optimal: 'Optimal', high: 'High workload', under: 'Underutilized' };
        chart.setOption({
            ...baseAnimation,
            grid: { left: 42, right: 16, top: 20, bottom: 66 },
            tooltip: { trigger: 'item', formatter: (p) => `${p.data[3]}<br/><em>Click to open profile</em>` },
            legend: {
                bottom: 4, itemWidth: 10, itemHeight: 10, textStyle: { fontSize: 11, color: '#4d5a6c' },
                data: Object.keys(nameOf).map(k => nameOf[k])
            },
            xAxis: {
                name: 'Average punch hours per accountable day', nameLocation: 'middle', nameGap: 22,
                nameTextStyle: { color: '#52514e', fontSize: 10 },
                min: 0, max: 12, splitLine: { lineStyle: { color: PALETTE.grid } },
                axisLine: { lineStyle: { color: PALETTE.axis } }, axisLabel: { fontSize: 10, color: PALETTE.muted }
            },
            yAxis: {
                min: 0, max: 130, axisLabel: { formatter: '{value}%', fontSize: 10, color: PALETTE.muted },
                splitLine: { lineStyle: { color: PALETTE.grid } }, axisLine: { show: false }
            },
            series: [
                ...Object.keys(byBand).map(band => ({
                    name: nameOf[band], type: 'scatter', symbolSize: 12, cursor: 'pointer',
                    itemStyle: { color: colorOf[band], borderColor: '#fff', borderWidth: 2 },
                    emphasis: { itemStyle: { borderWidth: 3, shadowBlur: 6, shadowColor: 'rgba(0,0,0,.25)' } },
                    data: byBand[band]
                })),
                {
                    type: 'line', silent: true, symbol: 'none', markLine: {
                        symbol: 'none', label: { formatter: '{b}', fontSize: 9, color: PALETTE.muted },
                        lineStyle: { type: 'dashed', color: PALETTE.muted },
                        data: [{ xAxis: ceilingX, label: { formatter: ceilingX + ' h/day' } }, { yAxis: targetY, label: { formatter: targetY + '% target' } }]
                    }, data: []
                }
            ]
        });
        chart.off('click');
        chart.on('click', (p) => { if (p.componentType === 'series' && p.data) drill(p.data[2]); });
    }

    function trend(id, categories, values, forecastValue) {
        const chart = ensure(id); if (!chart) return;
        const data = values.slice();
        const forecastSeries = forecastValue == null ? [] : [{
            name: 'Forecast', type: 'line', symbol: 'circle', symbolSize: 8,
            lineStyle: { color: PALETTE.blue, type: 'dashed', width: 2 },
            itemStyle: { color: '#fff', borderColor: PALETTE.blue, borderWidth: 2 },
            data: [...Array(categories.length - 1).fill(null), data[data.length - 1], forecastValue],
            label: { show: true, formatter: (p) => p.dataIndex === categories.length ? p.value.toFixed(1) : '', position: 'top', fontWeight: 700 }
        }];
        chart.setOption({
            ...baseAnimation,
            grid: { left: 40, right: 20, top: 24, bottom: 30 },
            tooltip: {
                trigger: 'axis',
                formatter: (params) => {
                    const label = params[0].axisValueLabel;
                    const rows = params.filter(p => p.value != null)
                        .map(p => `${p.seriesName}: <b>${(+p.value).toFixed(1)}</b>${label === 'Forecast' ? ' <em>(projected)</em>' : ''}`);
                    return `<strong>${label}</strong><br/>${rows.join('<br/>')}`;
                }
            },
            xAxis: {
                type: 'category', data: [...categories, ...(forecastValue == null ? [] : ['Forecast'])],
                axisLine: { lineStyle: { color: PALETTE.axis } }, axisLabel: { fontSize: 10, color: PALETTE.muted }
            },
            yAxis: {
                min: 0, max: 100, splitLine: { lineStyle: { color: PALETTE.grid } }, axisLine: { show: false },
                axisLabel: { fontSize: 10, color: PALETTE.muted }
            },
            series: [
                {
                    name: 'Team score', type: 'line', smooth: false, symbol: 'circle', symbolSize: 8,
                    lineStyle: { color: PALETTE.blue, width: 2 }, itemStyle: { color: PALETTE.blue },
                    data
                },
                ...forecastSeries
            ]
        });
    }

    function heatmap(id, xCategories, yCategories, cells, drillable) {
        const chart = ensure(id); if (!chart) return;
        chart.setOption({
            ...baseAnimation,
            tooltip: { position: 'top', formatter: (p) => `${p.data[3]}${drillable ? '<br/><em>Click to open profile</em>' : ''}` },
            grid: { left: 140, right: 12, top: 10, bottom: 30 },
            xAxis: { type: 'category', data: xCategories, splitArea: { show: false }, axisLabel: { fontSize: 10, color: PALETTE.muted }, axisLine: { lineStyle: { color: PALETTE.axis } } },
            yAxis: { type: 'category', data: yCategories, axisLabel: { fontSize: 11, color: '#2b3648' }, axisLine: { show: false }, splitArea: { show: false } },
            visualMap: {
                min: 0, max: 100, show: false,
                inRange: { color: ['#cde2fb', '#9ec5f4', '#6da7ec', '#3987e5', '#256abf', '#184f95', '#0d366b'] }
            },
            series: [{
                type: 'heatmap', data: cells.map(c => [c.x, c.y, c.value, c.tooltip]), cursor: drillable ? 'pointer' : 'default',
                label: { show: true, formatter: (p) => p.data[2] > 0 || p.data[2] === 0 ? p.data[2].toFixed(1) : '', color: '#fff', fontSize: 10, fontWeight: 600 },
                itemStyle: { borderColor: '#fff', borderWidth: 3, borderRadius: 4 },
                emphasis: { itemStyle: { shadowBlur: 8, shadowColor: 'rgba(0,0,0,.2)' } }
            }]
        });
        chart.off('click');
        if (drillable) chart.on('click', (p) => { if (p.componentType === 'series') drill(yCategories[p.data[1]]); });
    }

    function network(id, nodes, links, hubName) {
        const chart = ensure(id); if (!chart) return;
        const maxDegree = Math.max(1, ...nodes.map(n => n.received + n.given));
        const data = nodes.map(n => {
            const isHub = n.name === hubName;
            const degree = n.received + n.given;
            const size = isHub ? 54 : 26 + Math.round((degree / maxDegree) * 16);
            return {
                name: n.name, value: degree, tooltip: n.tooltip, symbolSize: size,
                itemStyle: isHub
                    ? { color: PALETTE.blue, borderColor: '#a9c9f2', borderWidth: 4 }
                    : { color: '#dbe7fb', borderColor: '#b9d1f4', borderWidth: 1.5 },
                label: {
                    show: true, formatter: n.initials, position: 'inside',
                    color: isHub ? '#ffffff' : '#0f4d99', fontSize: isHub ? 14 : 11, fontWeight: 700
                }
            };
        });
        chart.setOption({
            ...baseAnimation,
            tooltip: { formatter: (p) => p.dataType === 'node' || p.dataType === 'edge' ? `${p.data.tooltip}${p.dataType === 'node' ? '<br/><em>Click to open profile</em>' : ''}` : '' },
            series: [{
                type: 'graph', layout: 'force', roam: true, draggable: true, cursor: 'pointer',
                force: { repulsion: 220, edgeLength: [50, 110], gravity: 0.28, friction: 0.5 },
                lineStyle: { color: '#c7d2e2', curveness: 0.18, width: 1.3, opacity: 0.85 },
                emphasis: { focus: 'adjacency', lineStyle: { width: 2.4, color: PALETTE.blue }, label: { fontSize: 12 } },
                data, links,
                edgeSymbol: ['none', 'arrow'], edgeSymbolSize: 6
            }]
        });
        chart.off('click');
        chart.on('click', (p) => { if (p.dataType === 'node') drill(p.data.name); });
    }

    function bars(id, categories, values, colors, max, suffix, drillable) {
        const chart = ensure(id); if (!chart) return;
        const decimals = max != null && max <= 5 ? 2 : 1;
        chart.setOption({
            ...baseAnimation,
            grid: { left: 130, right: 46, top: 10, bottom: 10 },
            tooltip: {
                trigger: 'axis', axisPointer: { type: 'shadow' },
                formatter: (params) => {
                    const p = params[0];
                    const hint = drillable ? '<br/><em>Click to open profile</em>' : '';
                    return `<strong>${p.name}</strong><br/>${p.value.toFixed(decimals)}${suffix || ''}${hint}`;
                }
            },
            xAxis: { min: 0, max: max || null, splitLine: { lineStyle: { color: PALETTE.grid } }, axisLabel: { fontSize: 10, color: PALETTE.muted }, axisLine: { show: false } },
            yAxis: { type: 'category', data: categories, axisLabel: { fontSize: 11, color: '#2b3648' }, axisLine: { show: false }, axisTick: { show: false } },
            series: [{
                type: 'bar', cursor: drillable ? 'pointer' : 'default',
                data: values.map((v, i) => ({ value: v, itemStyle: { color: (colors && colors[i]) || PALETTE.blue, borderRadius: [0, 4, 4, 0] } })),
                barWidth: 14,
                label: { show: true, position: 'right', formatter: (p) => p.value.toFixed(decimals) + (suffix || ''), fontSize: 11, fontWeight: 600, color: '#2b3648' }
            }]
        });
        chart.off('click');
        if (drillable) chart.on('click', (p) => { if (p.componentType === 'series') drill(categories[p.dataIndex]); });
    }

    window.epaCharts = { ensure, dispose, gauge, radar, scatter, trend, heatmap, network, bars, registerDrilldown };
})();
