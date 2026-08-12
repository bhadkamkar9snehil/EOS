// EPA Performance Atlas renderers. Data comes from Razor; every colour is read live from the
// --color-* custom properties Tailwind's @theme block in wwwroot/tailwind-input.css generates —
// there is no separate palette to keep in sync. Keep the option structures deliberately
// conservative: these charts run inside WPF WebView2 as well as normal browsers.
(() => {
    const instances = new Map(), renderers = new Map(), peerConnectorObservers = new Map();
    let drilldownRef = null;

    const css = (name, fallback) => getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;
    const palette = () => ({
        series: Array.from({ length: 8 }, (_, i) => css(`--color-chart-${i + 1}`, '#0f5f7a')),
        operational: css('--color-petrol', '#0f5f7a'),
        attendance: css('--color-chart-5', '#2f8189'),
        timesheet: css('--color-chart-2', '#33619f'),
        approval: css('--color-chart-3', '#6f5bb0'),
        good: css('--color-good', '#2f8f52'), warning: css('--color-warning', '#a37f00'), serious: css('--color-serious', '#cf7118'), critical: css('--color-critical', '#cf2a1e'), missing: css('--color-missing', '#aca696'),
        grid: css('--color-line', '#ddd6c5'), axis: css('--color-line', '#ddd6c5'), ink: css('--color-ink', '#1a1a18'), inkSoft: css('--color-ink-soft', '#4e4c45'), muted: css('--color-muted', '#8a8578'),
        // Charts now sit in a recessed `well`, so the plot ground is the well colour,
        // not the plate's surface — labels and fills must contrast against that.
        surface: css('--color-well', '#e9e4d6'), plate: css('--color-surface', '#fff'),
        tooltip: css('--color-chassis', '#232a33'), tooltipText: css('--color-on-chassis', '#f2eee4'),
        reducedMotion: window.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches || false
    });
    const orange = () => css('--color-primary', '#f26a12');
    const chartStroke = role => ({ strong: 3, focus: 2, context: 1.5, hairline: 1 }[role] || 1);
    const rgbOf = hex => { const c = hex.replace('#', '').trim(); const f = c.length === 3 ? c.split('').map(x => x + x).join('') : c; const n = parseInt(f, 16); return [(n >> 16) & 255, (n >> 8) & 255, n & 255]; };
    const srgbLinear = v => { v /= 255; return v <= .04045 ? v / 12.92 : Math.pow((v + .055) / 1.055, 2.4); };
    const relLuminance = ([r, g, b]) => .2126 * srgbLinear(r) + .7152 * srgbLinear(g) + .0722 * srgbLinear(b);
    const contrastRatio = (hexA, hexB) => { const a = relLuminance(rgbOf(hexA)), b = relLuminance(rgbOf(hexB)); return (Math.max(a, b) + .05) / (Math.min(a, b) + .05); };
    const bestTextColor = fill => contrastRatio(fill, '#ffffff') >= contrastRatio(fill, '#101828') ? '#ffffff' : '#101828';
    const mixHex = (hexA, hexB, t) => { const [ar, ag, ab] = rgbOf(hexA), [br, bg, bb] = rgbOf(hexB); const m = v => Math.round(v); return `rgb(${m(ar + (br - ar) * t)},${m(ag + (bg - ag) * t)},${m(ab + (bb - ab) * t)})`; };
    const hexToRgba = (hex, alpha) => {
        const clean = hex.replace('#', '').trim();
        const full = clean.length === 3 ? clean.split('').map(c => c + c).join('') : clean;
        const int = parseInt(full, 16);
        if (Number.isNaN(int)) return `rgba(15,95,122,${alpha})`;
        return `rgba(${(int >> 16) & 255}, ${(int >> 8) & 255}, ${int & 255}, ${alpha})`;
    };
    // Quadrant tints come from fully resolved per-theme tokens rather than a semantic
    // colour composited at low alpha. Alpha math cannot serve both themes: 5% of a hue
    // over cream reads as a clean pastel band, but the same 5% over near-black turns
    // into a muddy olive/brown smear. These are picked per theme, not calculated.
    const zoneFill = role => {
        const token = {
            underused: '--color-zone-underused',
            overloaded: '--color-zone-overloaded',
            inconsistent: '--color-zone-inconsistent',
            balanced: '--color-zone-balanced'
        }[role];
        return token ? css(token, 'transparent') : 'transparent';
    };
    const compactViewport = () => window.innerWidth <= 1600 || window.innerHeight <= 900;
    const largeViewport = () => window.innerWidth >= 1900 && window.innerHeight >= 1000;

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
        padding: largeViewport() ? [10, 13] : [8, 10],
        textStyle: { color: p.tooltipText, fontSize: largeViewport() ? 14 : 12.5, lineHeight: largeViewport() ? 21 : 18 },
        extraCssText: 'box-shadow:0 8px 22px rgba(0,0,0,.22);border-radius:3px;'
    });
    const motion = () => palette().reducedMotion
        ? { animation: false, animationDuration: 0, animationDurationUpdate: 0 }
        : { animation: true, animationDuration: 620, animationEasing: 'cubicOut', animationDurationUpdate: 260 };
    const initials = name => String(name || '').split(/\s+/).filter(Boolean).slice(0, 2).map(x => x[0].toUpperCase()).join('');
    const signed = (value, digits = 1) => `${value > 0 ? '+' : ''}${(+value).toFixed(digits)}`;
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
        const validValues = data.filter(v => Number.isFinite(v));
        const validCount = validValues.length;
        const draw = (silent = false) => {
            const p = palette(), color = roleColor(role, p);
            if (validCount === 0) {
                chart.setOption({
                    animation: false,
                    graphic: [{ type: 'text', left: 2, top: 'middle', style: { text: 'No dated evidence', fill: p.muted, fontSize: largeViewport() ? 12 : 9.5 } }]
                }, true);
                return;
            }
            if (validCount === 1) {
                const value = Math.max(0, Math.min(100, validValues[0]));
                const target = role === 'operational' ? 75 : role === 'attendance' ? 95 : 95;
                chart.setOption({
                    animation: false,
                    grid: { left: 1, right: 3, top: largeViewport() ? 11 : 7, bottom: largeViewport() ? 10 : 6 },
                    xAxis: { type: 'value', min: 0, max: 100, show: false },
                    yAxis: { type: 'category', data: ['Current'], show: false },
                    tooltip: { show: false, triggerOn: 'none' },
                    series: [{
                        type: 'bar', data: [value], barWidth: largeViewport() ? 10 : 7,
                        silent: true, cursor: 'default',
                        showBackground: true,
                        backgroundStyle: { color: p.grid, opacity: .62 },
                        itemStyle: { color },
                        markLine: {
                            silent: true, symbol: 'none', label: { show: false },
                            lineStyle: { color: p.inkSoft, width: chartStroke('hairline'), type: 'dashed', opacity: .72 },
                            data: [{ xAxis: target }]
                        }
                    }]
                }, true);
                return;
            }
            chart.setOption({
                animation: false,
                grid: { left: 2, right: 3, top: 4, bottom: 3 },
                xAxis: { type: 'category', show: false, boundaryGap: false, data: data.map((_, i) => i) },
                yAxis: { type: 'value', show: false, min: 0, max: 100 },
                tooltip: { show: false, triggerOn: 'none' },
                series: [{
                    type: 'line',
                    data,
                    silent: true,
                    cursor: 'default',
                    connectNulls: true,
                    smooth: .22,
                    showSymbol: validCount <= 5,
                    symbol: 'circle',
                    symbolSize: largeViewport() ? 6.5 : 5,
                    lineStyle: { color, width: largeViewport() ? chartStroke('focus') : chartStroke('context') },
                    itemStyle: { color, borderColor: p.surface, borderWidth: chartStroke('hairline') },
                    emphasis: { disabled: true },
                    areaStyle: { color, opacity: .035 }
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
            hasPrevious: !!x.hasPrevious,
            showMovement: !!x.showMovement,
            score: +x.score || 0,
            exceptions: +x.exceptions || 0,
            missing: !!x.missing
        }));

        const draw = (silent = false) => {
            const p = palette(), selected = String(selectedName || '').toLowerCase(), compact = compactViewport(), large = largeViewport();
            const tails = source
                .filter(x => selected && String(x.name).toLowerCase() === selected && x.showMovement && !x.missing)
                .map(pt => {
                    return {
                        name: `Focused movement · ${pt.name}`,
                        type: 'line',
                        silent: false,
                        symbol: ['circle', 'circle'],
                        symbolSize: [7, 0],
                        data: [[pt.prevX, pt.prevY], [pt.x, pt.y]],
                        cursor: 'help',
                        tooltip: {
                            show: true,
                            formatter: `<strong>${pt.name}</strong><br/>Previous month: <b>${pt.prevX.toFixed(1)} h · ${pt.prevY.toFixed(1)}%</b><br/>Current month: <b>${pt.x.toFixed(1)} h · ${pt.y.toFixed(1)}%</b>`
                        },
                        lineStyle: {
                            color: orange(),
                            width: chartStroke('focus'),
                            opacity: .9
                        },
                        itemStyle: {
                            color: p.surface,
                            borderColor: orange(),
                            borderWidth: chartStroke('context')
                        },
                        emphasis: { lineStyle: { width: chartStroke('strong'), opacity: 1 } },
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
                        borderColor: d.missing ? p.missing : p.surface,
                        borderType: d.missing ? 'dashed' : 'solid',
                        borderWidth: isSelected ? chartStroke('strong') : chartStroke('focus'),
                        shadowBlur: isSelected ? 14 : 0,
                        shadowColor: 'rgba(242,106,18,.28)'
                    }
                };
            });

            chart.setOption({
                ...(silent ? { animation: false } : motion()),
                grid: compact
                    ? { left: 42, right: 16, top: 25, bottom: 43 }
                    : large
                        ? { left: 68, right: 30, top: 42, bottom: 66 }
                        : { left: 56, right: 24, top: 34, bottom: 56 },
                tooltip: {
                    ...tip(p),
                    trigger: 'item',
                    formatter: x => {
                        if (x.seriesName !== 'Engineers' || !x.data) return '';
                        const d = x.data;
                        const comparison = d.hasPrevious
                            ? `<br/><span style="opacity:.72">Previous month</span> <b>${(+d.prevX).toFixed(1)} h · ${(+d.prevY).toFixed(1)}%</b><br/><span style="opacity:.72">Change</span> <b>${signed(d.x - d.prevX, 1)} h · ${signed(d.y - d.prevY, 1)} pp</b>`
                            : '';
                        return `<strong>${d.name}</strong><br/>Score <b>${(+d.score).toFixed(1)}</b><br/>Punch hours <b>${(+d.x).toFixed(1)} h</b><br/>Utilization <b>${d.missing ? 'no monthly source' : (+d.y).toFixed(1) + '%'}</b><br/>Exceptions <b>${d.exceptions}</b>${comparison}<br/><span style="opacity:.72">Click to focus across the Atlas</span>`;
                    }
                },
                xAxis: {
                    type: 'value', min: 0, max: maxX, splitNumber: 6,
                    name: 'ACCOUNTABLE / PUNCH HOURS', nameLocation: 'middle', nameGap: compact ? 29 : large ? 42 : 34,
                    nameTextStyle: { color: p.inkSoft, fontSize: compact ? 10 : large ? 13.5 : 11.5, fontWeight: 600 },
                    axisLine: { show: true, lineStyle: { color: p.axis } },
                    axisTick: { show: false, lineStyle: { color: p.axis } },
                    axisLabel: { color: p.inkSoft, fontSize: compact ? 10 : large ? 13 : 12 },
                    splitLine: { show: false, lineStyle: { color: p.grid, type: 'dashed', opacity: .62 } }
                },
                yAxis: {
                    type: 'value', min: 0, max: 110, splitNumber: 5,
                    name: 'UTILIZATION (%)', nameLocation: 'end', nameGap: compact ? 9 : large ? 17 : 14,
                    nameTextStyle: { color: p.inkSoft, fontSize: compact ? 10 : large ? 13.5 : 11.5, fontWeight: 600, align: 'left' },
                    axisLine: { show: true, lineStyle: { color: p.axis } },
                    axisTick: { show: false, lineStyle: { color: p.axis } },
                    axisLabel: { color: p.inkSoft, fontSize: compact ? 10 : large ? 13 : 12 },
                    splitLine: { show: true, lineStyle: { color: p.grid, type: 'dashed', opacity: 1 } }
                },
                series: [
                    ...tails,
                    {
                        name: 'zones', type: 'line', silent: true, symbol: 'none', data: [],
                        markArea: {
                            silent: true,
                            label: { color: p.muted, fontSize: compact ? 9.5 : large ? 12.5 : 11, fontWeight: 400 },
                            itemStyle: { borderWidth: 0 },
                            data: [
                                [{ name: 'UNDERUSED', xAxis: 0, yAxis: target, itemStyle: { color: zoneFill('underused') } }, { xAxis: med, yAxis: 110 }],
                                [{ name: 'OVERLOADED', xAxis: med, yAxis: target, itemStyle: { color: zoneFill('overloaded') } }, { xAxis: maxX, yAxis: 110 }],
                                [{ name: 'INCONSISTENT', xAxis: 0, yAxis: 0, itemStyle: { color: zoneFill('inconsistent') } }, { xAxis: med, yAxis: target }],
                                [{ name: 'BALANCED', xAxis: med, yAxis: 0, itemStyle: { color: zoneFill('balanced') } }, { xAxis: maxX, yAxis: target }]
                            ]
                        },
                        markLine: {
                            silent: true,
                            symbol: 'none',
                            label: { show: false },
                            lineStyle: { color: p.axis, type: 'dashed', width: chartStroke('hairline'), opacity: .6 },
                            data: [{ xAxis: med }, { yAxis: target }]
                        }
                    },
                    {
                        name: 'Engineers', type: 'scatter', z: 4, data: scatterData, symbol: 'circle',
                        symbolSize: (value, params) => compact
                            ? Math.max(18, Math.min(39, 14 + (+params.data.score || 0) * .24))
                            : large
                                ? Math.max(30, Math.min(60, 23 + (+params.data.score || 0) * .34))
                                : Math.max(24, Math.min(50, 19 + (+params.data.score || 0) * .29)),
                        cursor: 'pointer',
                        label: {
                            show: true,
                            formatter: x => x.data.missing ? `{missing|${initials(x.data.name)}}` : `{normal|${initials(x.data.name)}}`,
                            fontWeight: 700,
                            fontSize: compact ? 9.5 : large ? 13 : 11,
                            rich: {
                                normal: { color: '#fff', fontWeight: 700 },
                                missing: { color: p.inkSoft, fontWeight: 700 }
                            }
                        },
                        emphasis: { scale: 1.12, itemStyle: { borderWidth: chartStroke('strong') } }
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
            const p = palette(), compact = compactViewport(), large = largeViewport();
            const rendered = rows.map((s, i) => {
                const isSelected = String(s.name || '').toLowerCase() === selected;
                const isAttention = !!s.attention;
                const color = isSelected ? p.critical : isAttention ? orange() : p.operational;
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
                        width: isSelected ? chartStroke('strong') : isAttention ? chartStroke('focus') : chartStroke('hairline'),
                        opacity: isSelected ? 1 : isAttention ? .92 : .48
                    },
                    itemStyle: { color },
                    emphasis: { focus: 'series', lineStyle: { width: chartStroke('strong'), opacity: 1 } },
                    endLabel: {
                        show: true,
                        formatter: x => x.value == null ? '' : `${initials(s.name)}  ${(+x.value).toFixed(0)}`,
                        color,
                        fontSize: compact ? (isSelected || isAttention ? 10 : 9) : large ? (isSelected || isAttention ? 13 : 11) : (isSelected || isAttention ? 11.5 : 9.5),
                        fontWeight: isSelected ? 750 : isAttention ? 650 : 520,
                        distance: compact ? 4 : large ? 8 : 6
                    },
                    labelLayout: { moveOverlap: 'shiftY' },
                    z: isSelected ? 5 : isAttention ? 3 : 1
                };
            });

            rendered.push({
                name: 'Team median', type: 'line', data: median || [], smooth: .24, showSymbol: false, silent: true,
                lineStyle: { color: p.ink, width: chartStroke('context'), type: 'dashed', opacity: .74 }, z: 2
            });

            chart.setOption({
                ...(silent ? { animation: false } : motion()),
                grid: compact ? { left: 35, right: 78, top: 12, bottom: 32 } : large ? { left: 52, right: 122, top: 22, bottom: 50 } : { left: 44, right: 104, top: 18, bottom: 42 },
                tooltip: {
                    ...tip(p), trigger: 'axis',
                    formatter: params => `<strong>${params[0]?.axisValueLabel || ''}</strong><br/>${params.filter(x => x.value != null).sort((a, b) => b.value - a.value).slice(0, 8).map(x => `${x.marker}${x.seriesName}: <b>${(+x.value).toFixed(1)}</b>`).join('<br/>')}`
                },
                xAxis: {
                    type: 'category', boundaryGap: false, data: categories || [],
                    axisLine: { lineStyle: { color: p.axis } }, axisTick: { show: false },
                    axisLabel: { color: p.inkSoft, fontSize: compact ? 10 : large ? 13 : 11.5, margin: compact ? 7 : large ? 12 : 10 }
                },
                yAxis: {
                    type: 'value', min: 0, max: 100, splitNumber: 4,
                    axisLine: { show: false, lineStyle: { color: p.axis } }, axisTick: { show: false },
                    axisLabel: { color: p.inkSoft, fontSize: large ? 13 : 11.5 },
                    splitLine: { lineStyle: { color: p.grid, opacity: 1 } }
                },
                series: rendered
            }, true);
            requestAnimationFrame(() => { if (!chart.isDisposed()) chart.resize(); });
        };

        draw();
        renderers.set(id, () => draw(true));
    }

    function peerNetwork(id, nodes, links, aspect = 'overall', selectedName) {
        const chart = ensure(id);
        if (!chart) return;
        const people = (nodes || []).map(x => ({ ...x, received: +x.received || 0, given: +x.given || 0 }));
        const relationships = (links || []).map(x => ({
            ...x,
            count: +x.count || 0,
            overall: +x.overall || 0,
            collaboration: +x.collaboration || 0,
            communication: +x.communication || 0,
            reliability: +x.reliability || 0,
            technicalHelp: +x.technicalHelp || 0
        }));
        const aspectLabels = { overall: 'Overall', collaboration: 'Collaboration', communication: 'Communication', reliability: 'Reliability', technicalHelp: 'Technical help' };

        const draw = (silent = false) => {
            const p = palette(), selected = String(selectedName || '').toLowerCase(), compact = compactViewport(), large = largeViewport();
            const ratingColor = value => value <= 0 ? p.axis : value >= 4.5 ? p.good : value >= 4 ? p.operational : value >= 3 ? orange() : p.critical;
            const adjacentNames = new Set(selected ? [selected] : []);
            if (selected) relationships.forEach(link => {
                const source = String(link.source || '').toLowerCase(), target = String(link.target || '').toLowerCase();
                if (source === selected) adjacentNames.add(target);
                if (target === selected) adjacentNames.add(source);
            });
            const graphNodes = people.map(person => {
                const personKey = String(person.name || '').toLowerCase();
                const focused = personKey === selected;
                const connected = !selected || adjacentNames.has(personKey);
                return {
                    ...person,
                    symbolSize: large
                        ? Math.max(34, Math.min(64, 30 + (person.received + person.given) * 1.3))
                        : Math.max(28, Math.min(54, 25 + (person.received + person.given) * 1.1)),
                    draggable: true,
                    itemStyle: {
                        color: focused ? orange() : person.hub && !selected ? orange() : p.operational,
                        borderColor: focused ? orange() : p.surface,
                        borderWidth: focused ? 4 : 2,
                        opacity: connected ? 1 : .18,
                        shadowBlur: focused ? 12 : 0,
                        shadowColor: 'rgba(242,106,18,.28)'
                    },
                    label: { opacity: connected ? 1 : .28 }
                };
            });
            const graphLinks = relationships.map(link => {
                const rating = +link[aspect] || +link.overall || 0;
                const source = String(link.source || '').toLowerCase(), target = String(link.target || '').toLowerCase();
                const connected = !selected || source === selected || target === selected;
                return {
                    ...link,
                    rating,
                    lineStyle: {
                        color: ratingColor(rating),
                        width: connected ? Math.max(chartStroke('hairline'), Math.min(chartStroke('strong'), .6 + link.count * .45 + rating * .18)) : chartStroke('hairline'),
                        opacity: connected ? (selected ? .86 : .5) : .035,
                        curveness: .12
                    },
                    emphasis: { lineStyle: { opacity: 1, width: chartStroke('strong') } }
                };
            });

            chart.setOption({
                ...(silent ? { animation: false } : motion()),
                tooltip: {
                    ...tip(p),
                    trigger: 'item',
                    formatter: item => {
                        if (item.dataType === 'edge') {
                            const d = item.data;
                            return `<strong>${d.source} → ${d.target}</strong><br/>${aspectLabels[aspect] || 'Overall'} <b>${(+d.rating).toFixed(2)} / 5</b><br/>Overall ${(+d.overall).toFixed(2)} · Collab ${(+d.collaboration).toFixed(2)} · Comms ${(+d.communication).toFixed(2)}<br/>Reliability ${(+d.reliability).toFixed(2)} · Tech help ${(+d.technicalHelp).toFixed(2)}`;
                        }
                        const d = item.data;
                        return `<strong>${d.name}</strong><br/><b>${d.received}</b> reviews received · <b>${d.given}</b> given<br/><span style="opacity:.72">Click to focus across the Atlas</span>`;
                    }
                },
                series: [{
                    name: 'Peer ratings', type: 'graph', layout: 'force', roam: true, draggable: true,
                    left: large ? '3%' : '4%', right: large ? '3%' : '4%', top: '4%', bottom: '4%',
                    center: ['50%', '50%'], zoom: large ? 1.08 : 1,
                    data: graphNodes, links: graphLinks,
                    force: { repulsion: large ? 620 : 360, edgeLength: large ? [125, 235] : [88, 175], gravity: large ? .035 : .055, friction: .7, layoutAnimation: !p.reducedMotion },
                    edgeSymbol: ['none', 'arrow'], edgeSymbolSize: [0, large ? 8 : 6],
                    label: { show: true, formatter: x => initials(x.data.name), color: '#fff', fontSize: large ? 12.5 : 10.5, fontWeight: 700 },
                    edgeLabel: { show: false },
                    emphasis: { focus: 'adjacency', scale: 1.08 },
                    select: { disabled: true }
                }]
            }, true);
            chart.off('click');
            chart.on('click', item => { if (item.dataType === 'node' && item.data?.name) drill(item.data.name); });
            requestAnimationFrame(() => { if (!chart.isDisposed()) chart.resize(); });
        };

        draw();
        renderers.set(id, () => draw(true));
    }

    function peerAspectHeatmap(id, aspects, rows) {
        const chart = ensure(id);
        if (!chart) return;
        const labels = aspects || [];
        const people = rows || [];
        const draw = (silent = false) => {
            const p = palette(), compact = compactViewport(), large = largeViewport();
            // A 1–5 rating is a semantic magnitude scale, so semantic colours are correct
            // here — but orange is reserved for indicator roles, so the low-mid stop uses
            // `serious` and the midpoint a neutral rather than borrowing the brand accent.
            const stops = [p.critical, p.serious, p.missing, p.operational, p.good];
            const heatColor = value => {
                const pos = Math.max(0, Math.min(4, Math.max(1, Math.min(5, value)) - 1));
                const lower = Math.floor(pos), upper = Math.min(4, lower + 1);
                return mixHex(stops[lower], stops[upper], pos - lower);
            };
            const data = [];
            people.forEach((row, y) => (row.values || []).forEach((value, x) => {
                const raw = +(row.rawValues || [])[x] || +value;
                if (value != null && +value > 0) {
                    const fill = heatColor(+value);
                    data.push({ value: [x, y, +value, raw], name: row.name, received: +row.received || 0, established: !!row.established, adjusted: +row.adjusted || 0, confidence: +row.confidence || 0, itemStyle: { color: fill }, label: { color: bestTextColor(fill) } });
                }
            }));
            chart.setOption({
                ...(silent ? { animation: false } : motion()),
                grid: compact ? { left: 118, right: 10, top: 8, bottom: 26 } : large ? { left: 170, right: 22, top: 14, bottom: 40 } : { left: 142, right: 18, top: 10, bottom: 34 },
                tooltip: {
                    ...tip(p),
                    formatter: item => `<strong>${item.data.name}</strong> · ${item.data.received} review${item.data.received === 1 ? '' : 's'}<br/>${labels[item.value[0]]} raw: <b>${(+item.value[3]).toFixed(2)}</b><br/>Evidence-adjusted: <b>${(+item.value[2]).toFixed(2)}</b><br/>Overall reliable floor: ${(+item.data.confidence).toFixed(2)} · ${item.data.established ? 'established' : 'provisional'}`
                },
                xAxis: { type: 'category', data: labels, position: 'top', axisLine: { lineStyle: { color: p.axis } }, axisTick: { show: false }, axisLabel: { color: p.inkSoft, fontSize: compact ? 9.5 : large ? 12.5 : 10.5, interval: 0 } },
                yAxis: { type: 'category', data: people.map(x => x.name), inverse: true, axisLine: { lineStyle: { color: p.axis } }, axisTick: { show: false }, axisLabel: { color: p.inkSoft, fontSize: compact ? 9.5 : large ? 12.5 : 10.5, width: compact ? 118 : large ? 182 : 150, overflow: 'truncate', formatter: name => { const row = people.find(x => x.name === name); return `${name}  ·  n=${+row?.received || 0}`; } } },
                series: [{
                    type: 'heatmap', data, cursor: 'pointer',
                    label: { show: true, formatter: item => (+item.value[2]).toFixed(2), fontSize: compact ? 9 : large ? 12 : 10, fontWeight: 650 },
                    itemStyle: { borderColor: p.surface, borderWidth: 2 },
                    emphasis: { itemStyle: { borderColor: p.ink, borderWidth: 2 } }
                }]
            }, true);
            chart.off('click');
            chart.on('click', item => { if (item.data?.name) drill(item.data.name); });
            requestAnimationFrame(() => { if (!chart.isDisposed()) chart.resize(); });
        };
        draw();
        renderers.set(id, () => draw(true));
    }

    function peerRelationshipConnectors(id, inbound, outbound, direction = 'both') {
        const root = document.getElementById(id);
        if (!root) return;
        const canvas = root.querySelector('canvas');
        const focus = root.querySelector('.prx-focus-card');
        if (!canvas || !focus) return;

        const draw = () => {
            const rect = root.getBoundingClientRect();
            if (rect.width <= 0 || rect.height <= 0) return;
            const scale = Math.max(1, window.devicePixelRatio || 1);
            canvas.width = Math.round(rect.width * scale);
            canvas.height = Math.round(rect.height * scale);
            canvas.style.width = `${rect.width}px`;
            canvas.style.height = `${rect.height}px`;
            const ctx = canvas.getContext('2d');
            ctx.setTransform(scale, 0, 0, scale, 0, 0);
            ctx.clearRect(0, 0, rect.width, rect.height);

            const focusRect = focus.getBoundingClientRect();
            const focusLeft = { x: focusRect.left - rect.left + 2, y: focusRect.top - rect.top + focusRect.height / 2 };
            const focusRight = { x: focusRect.right - rect.left - 2, y: focusLeft.y };
            const p = palette();
            const drawArrow = (start, end, rating, selected, muted, reciprocal) => {
                const strong = Number(rating) || 0;
                const base = strong < 3 ? orange() : strong >= 4 ? p.good : p.operational;
                ctx.save();
                ctx.globalAlpha = selected ? 1 : muted ? .16 : reciprocal ? .68 : .48;
                ctx.strokeStyle = selected ? orange() : base;
                ctx.fillStyle = selected ? orange() : base;
                ctx.lineWidth = selected ? 2.4 : Math.max(1, .7 + strong * .22);
                if (!reciprocal) ctx.setLineDash([5, 4]);
                const delta = Math.max(28, Math.abs(end.x - start.x) * .46);
                const c1 = { x: start.x + Math.sign(end.x - start.x) * delta, y: start.y };
                const c2 = { x: end.x - Math.sign(end.x - start.x) * delta * .62, y: end.y };
                ctx.beginPath();
                ctx.moveTo(start.x, start.y);
                ctx.bezierCurveTo(c1.x, c1.y, c2.x, c2.y, end.x, end.y);
                ctx.stroke();
                ctx.setLineDash([]);
                const angle = Math.atan2(end.y - c2.y, end.x - c2.x);
                const size = selected ? 7 : 5.5;
                ctx.beginPath();
                ctx.moveTo(end.x, end.y);
                ctx.lineTo(end.x - size * Math.cos(angle - Math.PI / 6), end.y - size * Math.sin(angle - Math.PI / 6));
                ctx.lineTo(end.x - size * Math.cos(angle + Math.PI / 6), end.y - size * Math.sin(angle + Math.PI / 6));
                ctx.closePath();
                ctx.fill();
                ctx.restore();
            };

            (inbound || []).forEach(item => {
                const row = document.getElementById(item.id);
                if (!row) return;
                const r = row.getBoundingClientRect();
                if (r.bottom < rect.top || r.top > rect.bottom) return;
                drawArrow({ x: r.right - rect.left, y: r.top - rect.top + r.height / 2 }, focusLeft, item.rating, item.selected, direction === 'outbound', true);
            });
            (outbound || []).forEach(item => {
                const row = document.getElementById(item.id);
                if (!row) return;
                const r = row.getBoundingClientRect();
                if (r.bottom < rect.top || r.top > rect.bottom) return;
                drawArrow(focusRight, { x: r.left - rect.left, y: r.top - rect.top + r.height / 2 }, item.rating, item.selected, direction === 'inbound', item.reciprocal);
            });
        };

        disposePeerRelationshipConnectors(id);
        const schedule = () => requestAnimationFrame(draw);
        const observer = new ResizeObserver(schedule);
        observer.observe(root);
        const scrollers = [...root.querySelectorAll('.prx-relationship-list')];
        scrollers.forEach(scroller => scroller.addEventListener('scroll', schedule, { passive: true }));
        peerConnectorObservers.set(id, { observer, scrollers, schedule });
        schedule();
    }

    function disposePeerRelationshipConnectors(id) {
        const entry = peerConnectorObservers.get(id);
        if (!entry) return;
        entry.observer.disconnect();
        entry.scrollers.forEach(scroller => scroller.removeEventListener('scroll', entry.schedule));
        peerConnectorObservers.delete(id);
    }

    function portraitHistory(id, categories, series) {
        const chart = ensure(id);
        if (!chart) return;
        const rows = series || [];
        const draw = (silent = false) => {
            const p = palette();
            chart.setOption({
                ...(silent ? { animation: false } : motion()),
                grid: { left: 48, right: 18, top: 35, bottom: 31 },
                tooltip: { ...tip(p), trigger: 'axis' },
                legend: { top: 0, itemWidth: 20, itemHeight: 3, textStyle: { color: p.inkSoft, fontSize: 12 } },
                xAxis: { type: 'category', boundaryGap: false, data: categories || [], axisLine: { lineStyle: { color: p.axis, width: 1.2 } }, axisTick: { show: false }, axisLabel: { fontSize: 12, color: p.inkSoft }, splitLine: { show: true, lineStyle: { color: p.grid, width: 1, type: 'dashed', opacity: .7 } } },
                yAxis: { min: 0, max: 100, interval: 20, axisLine: { show: false }, axisTick: { show: false }, axisLabel: { fontSize: 12, color: p.inkSoft }, splitLine: { show: true, lineStyle: { color: p.grid, width: 1.2, opacity: .9 } } },
                series: rows.map((s, i) => {
                    const color = s.role === 'operational' ? orange() : roleColor(s.role, p, i);
                    return {
                        name: s.name, type: 'line', data: s.values || [], connectNulls: true, smooth: .18,
                        symbol: s.role === 'operational' ? 'circle' : ['diamond', 'rect', 'triangle'][i % 3],
                        symbolSize: s.role === 'operational' ? 8 : 6,
                        lineStyle: { color, width: s.role === 'operational' ? 3.2 : 2, opacity: s.role === 'operational' ? 1 : .8 },
                        itemStyle: { color, borderColor: p.surface, borderWidth: 1.2 },
                        ...(s.role === 'operational' ? { areaStyle: { color, opacity: .035 } } : {})
                    };
                })
            }, true);
        };
        draw();
        renderers.set(id, () => draw(true));
    }

    function portraitWeekly(id, categories, rows) {
        const chart = ensure(id);
        if (!chart) return;
        const values = rows || [];
        const draw = (silent = false) => {
            const p = palette();
            if (!values.length) {
                chart.setOption({ graphic: [{ type: 'text', left: 'center', top: 'middle', style: { text: 'No weekly dated evidence', fill: p.muted, fontSize: 12 } }] }, true);
                return;
            }
            chart.setOption({
                ...(silent ? { animation: false } : motion()),
                grid: { left: 43, right: 43, top: 31, bottom: 29 },
                legend: { top: 0, itemWidth: 16, itemHeight: 3, textStyle: { color: p.inkSoft, fontSize: 11 }, data: ['Punch hours', 'Timesheet hours', 'Fill rate', 'Attendance'] },
                tooltip: { ...tip(p), trigger: 'axis' },
                xAxis: { type: 'category', data: categories || [], axisLine: { lineStyle: { color: p.axis } }, axisTick: { show: false }, axisLabel: { color: p.inkSoft, fontSize: 11 } },
                yAxis: [
                    { type: 'value', min: 0, name: 'HOURS', nameTextStyle: { color: p.muted, fontSize: 10.5 }, axisLabel: { color: p.inkSoft, fontSize: 10.5 }, splitLine: { lineStyle: { color: p.grid } } },
                    { type: 'value', min: 0, max: 100, name: '%', nameTextStyle: { color: p.muted, fontSize: 10.5 }, axisLabel: { color: p.inkSoft, fontSize: 10.5 }, splitLine: { show: false } }
                ],
                series: [
                    { name: 'Punch hours', type: 'bar', data: values.map(x => +x.punch || 0), barMaxWidth: 16, itemStyle: { color: p.operational, opacity: .82 } },
                    { name: 'Timesheet hours', type: 'bar', data: values.map(x => +x.timesheet || 0), barMaxWidth: 16, itemStyle: { color: p.timesheet, opacity: .46 } },
                    { name: 'Fill rate', type: 'line', yAxisIndex: 1, data: values.map(x => +x.fill || 0), symbolSize: 5, lineStyle: { color: orange(), width: 1.8 }, itemStyle: { color: orange() } },
                    { name: 'Attendance', type: 'line', yAxisIndex: 1, data: values.map(x => +x.attendance || 0), symbolSize: 4, lineStyle: { color: p.attendance, width: 1.5, type: 'dashed' }, itemStyle: { color: p.attendance } }
                ]
            }, true);
        };
        draw();
        renderers.set(id, () => draw(true));
    }

    function portraitPeerRadar(id, aspects, reviewers, employeeAverage, teamAverage) {
        const chart = ensure(id);
        if (!chart) return;
        const people = reviewers || [];
        const draw = (silent = false) => {
            const p = palette(), indicators = (aspects || []).map(name => ({ name, max: 5, min: 0 }));
            const reviewerData = people.map((row, index) => ({
                name: row.name, value: row.values || [],
                symbol: 'circle', symbolSize: 3,
                lineStyle: { color: p.axis, width: .8, opacity: .34 },
                itemStyle: { color: p.axis, opacity: .48 },
                areaStyle: { color: p.axis, opacity: .01 }
            }));
            reviewerData.push({ name: 'Team average', value: teamAverage || [], symbol: 'none', lineStyle: { color: p.operational, width: 1.7, type: 'dashed' }, areaStyle: { color: p.operational, opacity: .025 } });
            reviewerData.push({ name: 'Employee average', value: employeeAverage || [], symbol: 'circle', symbolSize: 5, lineStyle: { color: orange(), width: 2.4 }, itemStyle: { color: orange() }, areaStyle: { color: orange(), opacity: .1 } });
            chart.setOption({
                ...(silent ? { animation: false } : motion()),
                tooltip: { ...tip(p), trigger: 'item', formatter: item => `<strong>${item.name}</strong><br/>${(aspects || []).map((a, i) => `${a}: <b>${(+item.value[i] || 0).toFixed(1)}</b>`).join('<br/>')}` },
                radar: { center: ['50%', '52%'], radius: '54%', indicator: indicators, splitNumber: 5, axisName: { color: p.inkSoft, fontSize: 11.5 }, axisLine: { lineStyle: { color: p.grid } }, splitLine: { lineStyle: { color: p.grid } }, splitArea: { areaStyle: { color: ['transparent', 'rgba(0,0,0,.012)'] } } },
                series: [{ type: 'radar', data: reviewerData, emphasis: { focus: 'self' } }]
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

    function measureOverview() {
        const read = selector => {
            const el = document.querySelector(selector);
            if (!el) return `${selector}=missing`;
            const cs = getComputedStyle(el), rect = el.getBoundingClientRect();
            return `${selector}=${rect.width.toFixed(1)}x${rect.height.toFixed(1)};minH=${cs.minHeight};height=${cs.height};pad=${cs.paddingTop}/${cs.paddingBottom};font=${cs.fontSize};color=${cs.color};bg=${cs.backgroundColor};display=${cs.display};grid=${cs.gridTemplateColumns}`;
        };
        return [
            `viewport=${window.innerWidth}x${window.innerHeight};dpr=${window.devicePixelRatio};interface=atlas`,
            read('.nav-rail button.selected'), read('.nav-rail button.selected b'),
            read('.atlas-pulse'), read('.pulse-score'), read('.pulse-score-main > strong'),
            read('.atlas-primary'), read('#atlas-field'), read('.attention-row'), read('.atlas-secondary')
        ].join(' | ');
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

    window.epaAtlas = {
        registerDrilldown,
        sparkline: guard('sparkline', sparkline),
        performanceField: guard('performanceField', performanceField),
        movementRiver: guard('movementRiver', movementRiver),
        peerNetwork: guard('peerNetwork', peerNetwork),
        peerRelationshipConnectors: guard('peerRelationshipConnectors', peerRelationshipConnectors),
        disposePeerRelationshipConnectors,
        peerAspectHeatmap: guard('peerAspectHeatmap', peerAspectHeatmap),
        portraitHistory: guard('portraitHistory', portraitHistory),
        portraitWeekly: guard('portraitWeekly', portraitWeekly),
        portraitPeerRadar: guard('portraitPeerRadar', portraitPeerRadar),
        measureOverview,
        dispose,
        refreshTheme: refresh
    };
})();
