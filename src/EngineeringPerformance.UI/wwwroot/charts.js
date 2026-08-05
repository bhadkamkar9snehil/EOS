// Thin ECharts wrapper called from Blazor via IJSRuntime. One chart instance is
// kept per container id so a re-render updates in place (and animates) instead
// of tearing the chart down every refresh.
(function () {
    const PALETTE = {
        blue: '#0F6E9E', orange: '#C85A22', aqua: '#1E8C5A', violet: '#7A4BB0',
        good: '#3F7D55', warning: '#A9762A', serious: '#B4653C', critical: '#A94236',
        grid: '#E4E5E1', axis: '#B8BCB8', muted: '#737C78', ink: '#202623'
    };
    const BAND_COLOR = { good: PALETTE.good, warning: PALETTE.warning, serious: PALETTE.serious, critical: PALETTE.critical };

    // One consistent tooltip skin for every chart — a plain dark card, no series-colored
    // borders. Mismatched colored tooltip "speech bubbles" per chart read as unfinished.
    const TOOLTIP_BASE = {
        backgroundColor: '#2A3330', borderWidth: 0, padding: [9, 12],
        textStyle: { color: '#f3f5f8', fontSize: 12, lineHeight: 18 },
        extraCssText: 'box-shadow:0 8px 20px rgba(10,16,28,.28); border-radius:6px;'
    };

    const instances = new Map();
    const observers = new Map();

    // Bridge back into Blazor: whichever page is active registers itself via
    // registerDrilldown, and any chart click that names an employee routes through here.
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
                ...TOOLTIP_BASE,
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
                axisName: { color: '#4F5955', fontSize: 11 },
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
            tooltip: { ...TOOLTIP_BASE, trigger: 'item', formatter: (p) => `<strong>${p.data[3]}</strong><br/><span style="opacity:.7">Click the point to open the profile</span>` },
            legend: {
                bottom: 4, itemWidth: 10, itemHeight: 10, textStyle: { fontSize: 11, color: '#4F5955' },
                data: Object.keys(nameOf).map(k => nameOf[k])
            },
            xAxis: {
                name: 'Average punch hours per accountable day', nameLocation: 'middle', nameGap: 22,
                nameTextStyle: { color: '#4F5955', fontSize: 10 },
                min: 0, max: 12, splitLine: { lineStyle: { color: PALETTE.grid } },
                axisLine: { lineStyle: { color: PALETTE.axis } }, axisLabel: { fontSize: 10, color: PALETTE.muted }
            },
            yAxis: {
                min: 0, max: 130, axisLabel: { formatter: '{value}%', fontSize: 10, color: PALETTE.muted },
                splitLine: { lineStyle: { color: PALETTE.grid } }, axisLine: { show: false }
            },
            series: [
                ...Object.keys(byBand).map(band => ({
                    name: nameOf[band], type: 'scatter', symbolSize: 18, cursor: 'pointer',
                    itemStyle: { color: colorOf[band], borderColor: '#fff', borderWidth: 2 },
                    emphasis: { scale: 1.25, itemStyle: { borderWidth: 3, shadowBlur: 8, shadowColor: 'rgba(0,0,0,.3)' } },
                    data: byBand[band]
                })),
                {
                    type: 'line', silent: true, symbol: 'none', markLine: {
                        symbol: 'none', label: { formatter: '{b}', fontSize: 10, color: PALETTE.muted },
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
                ...TOOLTIP_BASE,
                trigger: 'axis',
                formatter: (params) => {
                    const label = params[0].axisValueLabel;
                    const rows = params.filter(p => p.value != null)
                        .map(p => `${p.seriesName}: <b>${(+p.value).toFixed(1)}</b>${label === 'Forecast' ? ' <span style="opacity:.7">(projected)</span>' : ''}`);
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

    // Several named percentage series over the same month axis, for one engineer's history.
    function multiline(id, categories, series) {
        const chart = ensure(id); if (!chart) return;
        chart.setOption({
            ...baseAnimation,
            grid: { left: 40, right: 20, top: 34, bottom: 30 },
            legend: { top: 0, itemWidth: 10, itemHeight: 10, textStyle: { fontSize: 11, color: '#4F5955' } },
            tooltip: {
                ...TOOLTIP_BASE, trigger: 'axis',
                formatter: (params) => {
                    const rows = params.filter(p => p.value != null).map(p => `${p.marker}${p.seriesName}: <b>${(+p.value).toFixed(1)}</b>`);
                    return `<strong>${params[0].axisValueLabel}</strong><br/>${rows.join('<br/>')}`;
                }
            },
            xAxis: { type: 'category', data: categories, axisLine: { lineStyle: { color: PALETTE.axis } }, axisLabel: { fontSize: 10, color: PALETTE.muted } },
            yAxis: { min: 0, max: 100, splitLine: { lineStyle: { color: PALETTE.grid } }, axisLine: { show: false }, axisLabel: { fontSize: 10, color: PALETTE.muted } },
            series: series.map(s => ({
                name: s.name, type: 'line', symbol: 'circle', symbolSize: 7, connectNulls: true,
                lineStyle: { color: s.color, width: 2 }, itemStyle: { color: s.color },
                data: s.values
            }))
        });
    }

    function heatmap(id, xCategories, yCategories, cells, drillable) {
        const chart = ensure(id); if (!chart) return;
        chart.setOption({
            ...baseAnimation,
            tooltip: { ...TOOLTIP_BASE, position: 'top', formatter: (p) => `<strong>${p.data.tooltip}</strong>${drillable ? '<br/><span style="opacity:.7">Click to open profile</span>' : ''}` },
            grid: { left: 140, right: 12, top: 10, bottom: 30 },
            xAxis: { type: 'category', data: xCategories, splitArea: { show: false }, axisLabel: { fontSize: 10, color: PALETTE.muted }, axisLine: { lineStyle: { color: PALETTE.axis } } },
            yAxis: { type: 'category', data: yCategories, axisLabel: { fontSize: 11, color: '#202623' }, axisLine: { show: false }, splitArea: { show: false } },
            visualMap: {
                min: 0, max: 100, show: false,
                inRange: { color: ['#DDE9EF', '#B9D4E1', '#8EBBCF', '#5F9DB9', '#3B80A1', '#1F6686', '#0E4C68'] }
            },
            series: [{
                // ECharts 6.1's heatmap visual-channel color mapping breaks when the data
                // array carries a 4th (non-numeric) element, so extra fields ride as a
                // named property on an object data item instead of a bare array element.
                type: 'heatmap', data: cells.map(c => ({ value: [c.x, c.y, c.value], tooltip: c.tooltip })), cursor: drillable ? 'pointer' : 'default',
                label: { show: true, formatter: (p) => p.data.value[2] > 0 || p.data.value[2] === 0 ? p.data.value[2].toFixed(1) : '', color: '#fff', fontSize: 10, fontWeight: 600 },
                itemStyle: { borderColor: '#fff', borderWidth: 3, borderRadius: 4 },
                emphasis: { itemStyle: { shadowBlur: 8, shadowColor: 'rgba(0,0,0,.2)' } }
            }]
        });
        chart.off('click');
        if (drillable) chart.on('click', (p) => { if (p.componentType === 'series') drill(yCategories[p.data.value[1]]); });
    }

    // Fixed ring layout: every node gets an equal slot around the circle, so the chart
    // always fills its container regardless of node count — no empty voids, no force
    // simulation settling into a small clump on one side.
    function network(id, nodes, links, hubName) {
        const chart = ensure(id); if (!chart) return;
        const maxDegree = Math.max(1, ...nodes.map(n => n.received + n.given));
        const data = nodes.map(n => {
            const isHub = n.name === hubName;
            const degree = n.received + n.given;
            const size = isHub ? 46 : 22 + Math.round((degree / maxDegree) * 14);
            return {
                name: n.name, value: degree, tooltip: n.tooltip, symbolSize: size,
                itemStyle: isHub
                    ? { color: PALETTE.blue, borderColor: '#a9c9f2', borderWidth: 3 }
                    : { color: '#DEE8EE', borderColor: '#BBD2DE', borderWidth: 1.5 },
                label: {
                    show: true, formatter: n.initials, position: 'inside',
                    color: isHub ? '#ffffff' : '#0E4C68', fontSize: isHub ? 13 : 10.5, fontWeight: 700
                }
            };
        });
        chart.setOption({
            ...baseAnimation,
            tooltip: { ...TOOLTIP_BASE, formatter: (p) => p.dataType === 'node' || p.dataType === 'edge' ? `<strong>${p.data.tooltip}</strong>${p.dataType === 'node' ? '<br/><span style="opacity:.7">Click to open profile</span>' : ''}` : '' },
            series: [{
                type: 'graph', layout: 'circular', roam: true, draggable: true, cursor: 'pointer',
                circular: { rotateLabel: false },
                lineStyle: { color: '#CBD1CE', curveness: 0.22, width: 1.2, opacity: 0.8 },
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
                ...TOOLTIP_BASE,
                trigger: 'axis', axisPointer: { type: 'shadow' },
                formatter: (params) => {
                    const p = params[0];
                    const hint = drillable ? '<br/><span style="opacity:.7">Click to open profile</span>' : '';
                    return `<strong>${p.name}</strong><br/>${p.value.toFixed(decimals)}${suffix || ''}${hint}`;
                }
            },
            xAxis: { min: 0, max: max || null, splitLine: { lineStyle: { color: PALETTE.grid } }, axisLabel: { fontSize: 10, color: PALETTE.muted }, axisLine: { show: false } },
            yAxis: { type: 'category', data: categories, axisLabel: { fontSize: 11, color: '#202623' }, axisLine: { show: false }, axisTick: { show: false } },
            series: [{
                type: 'bar', cursor: drillable ? 'pointer' : 'default',
                data: values.map((v, i) => ({ value: v, itemStyle: { color: (colors && colors[i]) || PALETTE.blue, borderRadius: [0, 4, 4, 0] } })),
                barWidth: 14,
                label: { show: true, position: 'right', formatter: (p) => p.value.toFixed(decimals) + (suffix || ''), fontSize: 11, fontWeight: 600, color: '#202623' }
            }]
        });
        chart.off('click');
        if (drillable) chart.on('click', (p) => { if (p.componentType === 'series') drill(categories[p.dataIndex]); });
    }

    // Vertical grouped/single-series bars over a category axis (e.g. months), as opposed
    // to bars() which lays out one series horizontally against a name axis.
    function verticalBars(id, categories, series, max, suffix) {
        const chart = ensure(id); if (!chart) return;
        chart.setOption({
            ...baseAnimation,
            grid: { left: 40, right: 20, top: 34, bottom: 30 },
            legend: { show: series.length > 1, top: 0, itemWidth: 10, itemHeight: 10, textStyle: { fontSize: 11, color: '#4F5955' } },
            tooltip: {
                ...TOOLTIP_BASE, trigger: 'axis', axisPointer: { type: 'shadow' },
                formatter: (params) => {
                    const rows = params.filter(p => p.value != null).map(p => `${p.marker}${p.seriesName}: <b>${(+p.value).toFixed(1)}${suffix || ''}</b>`);
                    return `<strong>${params[0].axisValueLabel}</strong><br/>${rows.join('<br/>')}`;
                }
            },
            xAxis: { type: 'category', data: categories, axisLine: { lineStyle: { color: PALETTE.axis } }, axisLabel: { fontSize: 10, color: PALETTE.muted } },
            yAxis: {
                min: 0, max: max || null, splitLine: { lineStyle: { color: PALETTE.grid } }, axisLine: { show: false },
                axisLabel: { fontSize: 10, color: PALETTE.muted, formatter: suffix ? '{value}' + suffix : '{value}' }
            },
            series: series.map(s => ({
                name: s.name, type: 'bar', barGap: '20%',
                itemStyle: { color: s.color, borderRadius: [3, 3, 0, 0] },
                data: s.values
            }))
        });
    }

    window.epaCharts = { ensure, dispose, gauge, radar, scatter, trend, multiline, heatmap, network, bars, verticalBars, registerDrilldown };
})();
