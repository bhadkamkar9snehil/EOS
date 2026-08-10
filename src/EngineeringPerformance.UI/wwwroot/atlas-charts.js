// EPA Performance Atlas renderers. Data comes from Razor; colour/contrast comes
// from the resolved theme contract. Keep the option structures deliberately
// conservative: these charts run inside WPF WebView2 as well as normal browsers.
(() => {
    const instances = new Map(), renderers = new Map();
    let drilldownRef = null;

    const css = (name, fallback) => getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;
    const realist = () => document.documentElement.dataset.skin === 'realist';
    const fallbackPalette = () => ({
        series: Array.from({ length: 8 }, (_, i) => css(`--chart-${i + 1}`, '#0f5f7a')),
        operational: css('--chart-operational', '#0f5f7a'),
        attendance: css('--chart-attendance', '#16794a'),
        timesheet: css('--chart-timesheet', '#0f6e9e'),
        approval: css('--chart-approval', '#7c3aed'),
        good: css('--good', '#16794a'), warning: css('--warn', '#946200'), serious: css('--serious', '#b45309'), critical: css('--critical', '#b42318'), missing: css('--chart-missing', '#667085'),
        grid: css('--chart-grid', '#d8dde2'), axis: css('--chart-axis', '#aeb7c1'), ink: css('--chart-ink', '#0f172a'), inkSoft: css('--ink-soft', '#475569'), muted: css('--chart-muted', '#64748b'),
        surface: css('--chart-surface', '#fff'), tooltip: css('--chart-tooltip', '#0f172a'), tooltipText: css('--on-chart-tooltip', '#fff'),
        reducedMotion: window.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches || false
    });
    const palette = () => {
        const fallback = fallbackPalette();
        const resolved = window.epaTheme?.chartPalette?.() || {};
        return { ...fallback, ...resolved, series: Array.isArray(resolved.series) && resolved.series.length ? resolved.series : fallback.series };
    };
    const orange = () => css('--atlas-orange', '#f26a12');

    function ensure(id) {
        const el = document.getElementById(id);
        if (!el) return null;
        let chart = instances.get(id);
        if (chart && !chart.isDisposed()) return chart;
        chart = echarts.init(el, null, { renderer: 'svg' });
        instances.set(id, chart);
        const ro = new ResizeObserver(() => chart.resize());
        ro.observe(el);
        chart.__atlasResizeObserver = ro;
        requestAnimationFrame(() => { if (!chart.isDisposed()) chart.resize(); });
        return chart;
    }

    const tip = p => ({
        backgroundColor: p.tooltip,
        borderWidth: 0,
        padding: [8, 10],
        textStyle: { color: p.tooltipText, fontSize: 12.5, lineHeight: 18 },
        extraCssText: 'box-shadow:0 8px 22px rgba(0,0,0,.22);border-radius:3px;'
    });
    const motion = () => palette().reducedMotion
        ? { animation: false, animationDuration: 0, animationDurationUpdate: 0 }
        : { animation: true, animationDuration: 620, animationEasing: 'cubicOut', animationDurationUpdate: 260 };
    const initials = name => String(name || '').split(/\s+/).filter(Boolean).slice(0, 2).map(x => x[0].toUpperCase()).join('');
    const bandColor = (band, p) => {
        switch (String(band || '').toLowerCase()) {
            case 'critical': return p.critical;
            case 'serious': return orange();
            default: return p.operational;
        }
    };
    const roleColor = (role, p, index = 0) => {
        switch (String(role || '').toLowerCase()) {
            case 'attendance': return p.attendance;
            case 'timesheet': return p.timesheet;
            case 'approval': return p.approval;
            case 'warning': return p.warning;
            case 'serious': return p.serious;
            case 'critical': return p.critical;
            default: return p.series[index % p.series.length] || p.operational;
        }
    };

    function registerDrilldown(ref) { drilldownRef = ref; }
    function drill(name) {
        if (!drilldownRef || !name) return;
        drilldownRef.invokeMethodAsync('DrilldownTo', name).catch(() => {});
    }

    function sparkline(id, values, role = 'operational') {
        const chart = ensure(id);
        if (!chart) return;
        const data = (values || []).map(v => v == null ? null : Number(v));
        const validCount = data.filter(v => Number.isFinite(v)).length;
        const draw = (silent = false) => {
            const p = palette(), color = roleColor(role, p), physical = realist();
            chart.setOption({
                ...(silent ? { animation: false } : motion()),
                grid: { left: 2, right: 3, top: 4, bottom: 3 },
                xAxis: { type: 'category', show: false, boundaryGap: false, data: data.map((_, i) => i) },
                yAxis: { type: 'value', show: false, min: 0, max: 100 },
                tooltip: { show: false },
                series: [{
                    type: 'line',
                    data,
                    connectNulls: true,
                    smooth: .22,
                    symbol: validCount <= 1 ? 'circle' : 'none',
                    symbolSize: physical ? 6 : 5,
                    lineStyle: { color, width: physical ? 2.25 : 1.8 },
                    itemStyle: { color, borderColor: p.surface, borderWidth: 1.2 },
                    areaStyle: { color, opacity: physical ? .08 : .035 },
                    markLine: physical ? { silent: true, symbol: 'none', label: { show: false }, lineStyle: { color: p.axis, opacity: .25 }, data: [{ yAxis: 50 }] } : undefined
                }]
            }, true);
        };
        draw();
        renderers.set(id, () => draw(true));
    }

    function performanceField(id, points, xMax, utilizationTarget, medianX, selectedName) {
        const chart = ensure(id);
        if (!chart) return;
        const source = (points || []).map(x => ({
            ...x,
            x: +x.x || 0,
            y: +x.y || 0,
            prevX: x.prevX == null ? null : +x.prevX,
            prevY: x.prevY == null ? null : +x.prevY,
            score: +x.score || 0,
            exceptions: +x.exceptions || 0,
            missing: !!x.missing
        }));

        const draw = (silent = false) => {
            const p = palette(), selected = String(selectedName || '').toLowerCase(), physical = realist();
            const tails = source
                .filter(x => x.prevX > 0 && x.prevY > 0 && !x.missing)
                .map((pt, i) => {
                    const isSelected = String(pt.name).toLowerCase() === selected;
                    return {
                        name: `move-${i}`,
                        type: 'line',
                        silent: true,
                        symbol: ['circle', 'circle'],
                        symbolSize: [physical ? 7 : 5, 0],
                        data: [[pt.prevX, pt.prevY], [pt.x, pt.y]],
                        lineStyle: {
                            color: isSelected ? orange() : p.axis,
                            width: isSelected ? 2.2 : physical ? 1.25 : .9,
                            opacity: isSelected ? .92 : physical ? .5 : .34
                        },
                        itemStyle: {
                            color: p.surface,
                            borderColor: isSelected ? orange() : p.axis,
                            borderWidth: physical ? 1.5 : 1.1
                        },
                        z: 1
                    };
                });

            const rawMax = Math.max(1, +xMax || Math.max(...source.map(x => x.x), 1) * 1.12);
            const interval = rawMax > 260 ? 50 : rawMax > 120 ? 25 : 10;
            const maxX = Math.max(20, Math.ceil(rawMax / interval) * interval);
            const target = +utilizationTarget || 75;
            const med = +medianX || maxX / 2;
            const scatterData = source.map(d => {
                const isSelected = String(d.name || '').toLowerCase() === selected;
                return {
                    ...d,
                    value: [d.x, d.y],
                    itemStyle: {
                        color: d.missing ? p.surface : isSelected ? orange() : bandColor(d.band, p),
                        borderColor: d.missing ? p.missing : physical ? '#f4ead8' : p.surface,
                        borderType: d.missing ? 'dashed' : 'solid',
                        borderWidth: isSelected ? 4 : physical ? 2.4 : 2,
                        shadowBlur: isSelected ? 14 : 0,
                        shadowColor: 'rgba(242,106,18,.28)'
                    }
                };
            });

            chart.setOption({
                ...(silent ? { animation: false } : motion()),
                grid: { left: physical ? 64 : 56, right: physical ? 28 : 24, top: physical ? 38 : 34, bottom: physical ? 60 : 56 },
                tooltip: {
                    ...tip(p),
                    trigger: 'item',
                    formatter: x => {
                        if (x.seriesName !== 'Engineers' || !x.data) return '';
                        const d = x.data;
                        return `<strong>${d.name}</strong><br/>Score <b>${(+d.score).toFixed(1)}</b><br/>Punch hours <b>${(+d.x).toFixed(1)} h</b><br/>Utilization <b>${d.missing ? 'no monthly source' : (+d.y).toFixed(1) + '%'}</b><br/>Exceptions <b>${d.exceptions}</b><br/><span style="opacity:.72">Click to focus across the Atlas</span>`;
                    }
                },
                xAxis: {
                    type: 'value', min: 0, max: maxX, splitNumber: 6,
                    name: 'ACCOUNTABLE / PUNCH HOURS', nameLocation: 'middle', nameGap: physical ? 38 : 34,
                    nameTextStyle: { color: p.inkSoft, fontSize: physical ? 12 : 11.5, fontWeight: 600 },
                    axisLine: { show: true, lineStyle: { color: p.axis } },
                    axisTick: { show: physical, lineStyle: { color: p.axis } },
                    axisLabel: { color: p.inkSoft, fontSize: 12 },
                    splitLine: { show: physical, lineStyle: { color: p.grid, type: 'dashed', opacity: .62 } }
                },
                yAxis: {
                    type: 'value', min: 0, max: 110, splitNumber: 5,
                    name: 'UTILIZATION (%)', nameLocation: 'end', nameGap: 14,
                    nameTextStyle: { color: p.inkSoft, fontSize: physical ? 12 : 11.5, fontWeight: 600, align: 'left' },
                    axisLine: { show: true, lineStyle: { color: p.axis } },
                    axisTick: { show: physical, lineStyle: { color: p.axis } },
                    axisLabel: { color: p.inkSoft, fontSize: 12 },
                    splitLine: { show: true, lineStyle: { color: p.grid, type: 'dashed', opacity: physical ? .68 : 1 } }
                },
                series: [
                    ...tails,
                    {
                        name: 'zones', type: 'line', silent: true, symbol: 'none', data: [],
                        markArea: {
                            silent: true,
                            label: { color: p.muted, fontSize: physical ? 11.5 : 11, fontWeight: physical ? 600 : 400 },
                            itemStyle: { color: 'transparent', borderWidth: 0 },
                            data: [
                                [{ name: 'UNDERUSED', xAxis: 0, yAxis: target }, { xAxis: med, yAxis: 110 }],
                                [{ name: 'OVERLOADED', xAxis: med, yAxis: target }, { xAxis: maxX, yAxis: 110 }],
                                [{ name: 'INCONSISTENT', xAxis: 0, yAxis: 0 }, { xAxis: med, yAxis: target }],
                                [{ name: 'BALANCED', xAxis: med, yAxis: 0 }, { xAxis: maxX, yAxis: target }]
                            ]
                        },
                        markLine: {
                            silent: true,
                            symbol: 'none',
                            label: { show: false },
                            lineStyle: { color: p.axis, type: 'dashed', width: physical ? 1.2 : 1, opacity: physical ? .72 : .52 },
                            data: [{ xAxis: med }, { yAxis: target }]
                        }
                    },
                    {
                        name: 'Engineers', type: 'scatter', z: 4, data: scatterData, symbol: 'circle',
                        symbolSize: (value, params) => Math.max(24, Math.min(50, 19 + (+params.data.score || 0) * .29)),
                        cursor: 'pointer',
                        label: {
                            show: true,
                            formatter: x => x.data.missing ? `{missing|${initials(x.data.name)}}` : `{normal|${initials(x.data.name)}}`,
                            fontWeight: 700,
                            fontSize: physical ? 11.5 : 11,
                            rich: {
                                normal: { color: '#fff', fontWeight: 700 },
                                missing: { color: p.inkSoft, fontWeight: 700 }
                            }
                        },
                        emphasis: { scale: 1.12, itemStyle: { borderWidth: 3 } }
                    }
                ]
            }, true);

            chart.off('click');
            chart.on('click', x => { if (x.seriesName === 'Engineers' && x.data) drill(x.data.name); });
            requestAnimationFrame(() => { if (!chart.isDisposed()) chart.resize(); });
        };

        draw();
        renderers.set(id, () => draw(true));
    }

    function movementRiver(id, categories, series, median, selectedName) {
        const chart = ensure(id);
        if (!chart) return;
        const rows = series || [], selected = String(selectedName || '').toLowerCase();

        const draw = (silent = false) => {
            const p = palette(), physical = realist();
            const rendered = rows.map((s, i) => {
                const isSelected = String(s.name || '').toLowerCase() === selected;
                const isAttention = !!s.attention;
                const color = isSelected ? orange() : isAttention ? p.operational : p.axis;
                return {
                    name: s.name,
                    type: 'line',
                    data: s.values || [],
                    smooth: .24,
                    connectNulls: true,
                    showSymbol: false,
                    symbol: 'circle',
                    lineStyle: {
                        color,
                        width: isSelected ? 2.7 : isAttention ? (physical ? 1.55 : 1.4) : .8,
                        opacity: isSelected ? 1 : isAttention ? (physical ? .72 : .68) : .18
                    },
                    itemStyle: { color },
                    emphasis: { focus: 'series', lineStyle: { width: 2.4, opacity: 1 } },
                    endLabel: {
                        show: isSelected || isAttention,
                        formatter: x => x.value == null ? '' : `${initials(s.name)}  ${(+x.value).toFixed(0)}`,
                        color,
                        fontSize: 11.5,
                        fontWeight: isSelected ? 700 : 600,
                        distance: 6
                    },
                    labelLayout: { moveOverlap: 'shiftY' },
                    z: isSelected ? 5 : isAttention ? 3 : 1
                };
            });

            rendered.push({
                name: 'Team median', type: 'line', data: median || [], smooth: .24, showSymbol: false, silent: true,
                lineStyle: { color: p.ink, width: physical ? 1.7 : 1.5, type: 'dashed', opacity: .74 }, z: 2
            });

            chart.setOption({
                ...(silent ? { animation: false } : motion()),
                grid: { left: 44, right: 82, top: 18, bottom: 42 },
                tooltip: {
                    ...tip(p), trigger: 'axis',
                    formatter: params => `<strong>${params[0]?.axisValueLabel || ''}</strong><br/>${params.filter(x => x.value != null).sort((a, b) => b.value - a.value).slice(0, 8).map(x => `${x.marker}${x.seriesName}: <b>${(+x.value).toFixed(1)}</b>`).join('<br/>')}`
                },
                xAxis: {
                    type: 'category', boundaryGap: false, data: categories || [],
                    axisLine: { lineStyle: { color: p.axis } }, axisTick: { show: physical },
                    axisLabel: { color: p.inkSoft, fontSize: 11.5, margin: 10 }
                },
                yAxis: {
                    type: 'value', min: 0, max: 100, splitNumber: 4,
                    axisLine: { show: physical, lineStyle: { color: p.axis } }, axisTick: { show: false },
                    axisLabel: { color: p.inkSoft, fontSize: 11.5 },
                    splitLine: { lineStyle: { color: p.grid, opacity: physical ? .7 : 1 } }
                },
                series: rendered
            }, true);
            requestAnimationFrame(() => { if (!chart.isDisposed()) chart.resize(); });
        };

        draw();
        renderers.set(id, () => draw(true));
    }

    function portraitHistory(id, categories, series) {
        const chart = ensure(id);
        if (!chart) return;
        const rows = series || [];
        const draw = (silent = false) => {
            const p = palette();
            chart.setOption({
                ...(silent ? { animation: false } : motion()),
                grid: { left: 44, right: 20, top: 30, bottom: 36 },
                tooltip: { ...tip(p), trigger: 'axis' },
                legend: { top: 0, itemWidth: 18, itemHeight: 2, textStyle: { color: p.inkSoft, fontSize: 11 } },
                xAxis: { type: 'category', data: categories || [], axisLine: { lineStyle: { color: p.axis } }, axisTick: { show: false }, axisLabel: { fontSize: 11, color: p.inkSoft } },
                yAxis: { min: 0, max: 100, axisLine: { show: false }, axisTick: { show: false }, axisLabel: { fontSize: 11, color: p.inkSoft }, splitLine: { lineStyle: { color: p.grid } } },
                series: rows.map((s, i) => {
                    const color = s.role === 'operational' ? orange() : roleColor(s.role, p, i);
                    return {
                        name: s.name, type: 'line', data: s.values || [], connectNulls: true, smooth: .18,
                        symbol: s.role === 'operational' ? 'circle' : ['diamond', 'rect', 'triangle'][i % 3],
                        symbolSize: 6,
                        lineStyle: { color, width: s.role === 'operational' ? 2.4 : 1.4, opacity: s.role === 'operational' ? 1 : .72 },
                        itemStyle: { color }
                    };
                })
            }, true);
        };
        draw();
        renderers.set(id, () => draw(true));
    }

    function dispose(id) {
        const c = instances.get(id);
        if (c && !c.isDisposed()) c.dispose();
        if (c?.__atlasResizeObserver) c.__atlasResizeObserver.disconnect();
        instances.delete(id);
        renderers.delete(id);
    }

    function refresh() {
        for (const [id, render] of [...renderers]) {
            const c = instances.get(id);
            if (!c || c.isDisposed()) { renderers.delete(id); continue; }
            try { render(); }
            catch (error) { console.error(`[EPA Atlas] refresh failed for ${id}`, error); }
        }
    }

    const guard = (name, fn) => (...args) => {
        try {
            const result = fn(...args);
            const id = args[0], el = typeof id === 'string' ? document.getElementById(id) : null;
            if (el) { delete el.dataset.chartError; el.removeAttribute('title'); }
            return result;
        }
        catch (error) {
            const id = args[0];
            console.error(`[EPA Atlas] ${name} failed`, error);
            const el = typeof id === 'string' ? document.getElementById(id) : null;
            if (el) {
                el.dataset.chartError = 'true';
                el.title = `Chart rendering failed: ${error?.message || error}`;
            }
            return null;
        }
    };

    window.addEventListener('epa-theme-changed', refresh);
    window.addEventListener('epa-motion-changed', refresh);
    window.addEventListener('epa-skin-changed', refresh);

    window.epaAtlas = {
        registerDrilldown,
        sparkline: guard('sparkline', sparkline),
        performanceField: guard('performanceField', performanceField),
        movementRiver: guard('movementRiver', movementRiver),
        portraitHistory: guard('portraitHistory', portraitHistory),
        dispose,
        refreshTheme: refresh
    };
})();
