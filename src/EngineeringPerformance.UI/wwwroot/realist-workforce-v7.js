// Reference-calibrated Realist workforce field. The base Atlas renderer still owns
// data, tooltips and drill-down registration; this layer only replaces the
// physical rendering while the Realist skin is active.
(() => {
    const atlas = window.epaAtlas;
    if (!atlas?.performanceField || !window.echarts) return;

    const original = atlas.performanceField.bind(atlas);
    const lastArgs = new Map();
    const css = (name, fallback) => getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;
    const isRealist = () => document.documentElement.dataset.skin === 'realist' || document.documentElement.dataset.epaSkin === 'realist';
    const initials = name => String(name || '').split(/\s+/).filter(Boolean).slice(0, 2).map(x => x[0].toUpperCase()).join('');
    const font = '"Bahnschrift Condensed","Arial Narrow","DejaVu Sans Condensed",sans-serif';

    const faceGradient = (kind) => {
        const sets = {
            navy: ['#3c7286', '#17495d', '#092d3d'],
            orange: ['#f5a04f', '#df6d10', '#9b3904'],
            red: ['#e26458', '#b62c21', '#78150f'],
            missing: ['#f1e9dc', '#d8cdbb', '#b8aa95']
        };
        const s = sets[kind] || sets.navy;
        return new echarts.graphic.RadialGradient(.34, .28, .88, [
            { offset: 0, color: s[0] },
            { offset: .42, color: s[1] },
            { offset: 1, color: s[2] }
        ]);
    };

    const tone = d => {
        const band = String(d.band || '').toLowerCase();
        if (d.missing) return 'missing';
        if (band === 'critical') return 'red';
        if (band === 'serious' || band === 'warning') return 'orange';
        return 'navy';
    };

    function physicalOption(points, xMax, utilizationTarget, medianX, selectedName) {
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
        const selected = String(selectedName || '').toLowerCase();
        const rawMax = Math.max(60, +xMax || Math.max(...source.map(x => x.x), 60) * 1.14);
        const step = rawMax <= 240 ? 20 : rawMax <= 360 ? 30 : 50;
        const maxX = Math.ceil(rawMax / step) * step;
        const target = Math.max(0, Math.min(100, +utilizationTarget || 60));
        const med = Math.max(0, Math.min(maxX, +medianX || maxX / 2));
        const ink = '#332f29';
        const inkSoft = '#504b43';
        const rule = '#8f8779';
        const cream = '#e8dece';
        const orange = css('--atlas-orange', '#ed7415');

        const baseSize = d => Math.max(25, Math.min(31, 20 + d.score * .115));
        const selectedSize = d => baseSize(d) + 11;

        const tails = source
            .filter(d => d.prevX > 0 && d.prevY > 0 && !d.missing)
            .map((d, i) => {
                const chosen = String(d.name || '').toLowerCase() === selected;
                return {
                    name: `history-${i}`,
                    type: 'line',
                    silent: true,
                    symbol: ['circle', 'none'],
                    symbolSize: [chosen ? 10 : 9, 0],
                    data: [[d.prevX, d.prevY], [d.x, d.y]],
                    lineStyle: { color: chosen ? '#bc5a15' : '#8c8273', width: chosen ? 1.55 : 1.05, opacity: chosen ? .94 : .78 },
                    itemStyle: { color: cream, borderColor: chosen ? '#b95612' : '#8d8374', borderWidth: 1.15 },
                    z: 1
                };
            });

        const haloData = source.filter(d => String(d.name || '').toLowerCase() === selected && !d.missing).map(d => ({
            value: [d.x, d.y],
            symbolSize: selectedSize(d) + 18,
            itemStyle: {
                color: 'rgba(236,116,20,.10)',
                borderColor: 'rgba(236,116,20,.34)',
                borderWidth: 1.2,
                shadowBlur: 22,
                shadowColor: 'rgba(222,104,18,.52)'
            }
        }));

        const bezelData = source.map(d => {
            const chosen = String(d.name || '').toLowerCase() === selected;
            const size = chosen ? selectedSize(d) : baseSize(d);
            return {
                value: [d.x, d.y],
                symbolSize: size + 6,
                itemStyle: {
                    color: chosen ? '#a85a24' : '#353934',
                    borderColor: chosen ? '#f0c593' : '#bfb5a3',
                    borderWidth: chosen ? 1.6 : 1.15,
                    shadowBlur: chosen ? 10 : 5,
                    shadowOffsetY: chosen ? 4 : 3,
                    shadowColor: 'rgba(0,0,0,.58)'
                }
            };
        });

        const faceData = source.map(d => {
            const chosen = String(d.name || '').toLowerCase() === selected;
            const size = chosen ? selectedSize(d) : baseSize(d);
            return {
                ...d,
                value: [d.x, d.y],
                symbolSize: size,
                itemStyle: {
                    color: faceGradient(chosen ? 'orange' : tone(d)),
                    borderColor: d.missing ? '#70695f' : chosen ? '#fff1d7' : '#111716',
                    borderType: d.missing ? 'dashed' : 'solid',
                    borderWidth: chosen ? 2.2 : 1.35,
                    shadowBlur: chosen ? 10 : 3,
                    shadowOffsetY: 2,
                    shadowColor: chosen ? 'rgba(177,74,4,.42)' : 'rgba(0,0,0,.36)',
                    opacity: 1
                },
                label: {
                    show: true,
                    formatter: initials(d.name),
                    color: d.missing ? '#48433d' : '#fff7e8',
                    fontFamily: font,
                    fontWeight: 700,
                    fontSize: chosen ? 16 : 11.5,
                    textShadowColor: 'rgba(0,0,0,.72)',
                    textShadowBlur: 2,
                    textShadowOffsetY: 1
                }
            };
        });

        const quadrantLabels = [
            { value: [6, 94], label: { position: 'right', formatter: 'UNDERUSED' } },
            { value: [maxX - 6, 94], label: { position: 'left', formatter: 'OVERLOADED' } },
            { value: [6, 11], label: { position: 'right', formatter: 'INCONSISTENT' } },
            { value: [maxX - 6, 15], label: { position: 'left', formatter: 'BALANCED' } }
        ];

        return {
            animation: false,
            grid: { left: 58, right: 28, top: 10, bottom: 48 },
            tooltip: {
                backgroundColor: '#151817',
                borderColor: '#5d5b54',
                borderWidth: 1,
                padding: [8, 10],
                textStyle: { color: '#f4ead8', fontFamily: font, fontSize: 12, lineHeight: 18 },
                extraCssText: 'box-shadow:0 8px 18px rgba(0,0,0,.42);border-radius:3px;',
                trigger: 'item',
                formatter: x => {
                    if (x.seriesName !== 'Engineers' || !x.data) return '';
                    const d = x.data;
                    return `<strong>${d.name}</strong><br/>Score <b>${(+d.score).toFixed(1)}</b><br/>Punch hours <b>${(+d.x).toFixed(1)} h</b><br/>Utilization <b>${d.missing ? 'no monthly source' : (+d.y).toFixed(1) + '%'}</b><br/>Exceptions <b>${d.exceptions}</b>`;
                }
            },
            xAxis: {
                type: 'value', min: 0, max: maxX, interval: step,
                name: 'ACCOUNTABLE HOURS (30 DAYS)', nameLocation: 'middle', nameGap: 28,
                nameTextStyle: { color: ink, fontFamily: font, fontSize: 10.5, fontWeight: 650 },
                axisLine: { show: true, lineStyle: { color: '#514a40', width: 1.05 } },
                axisTick: { show: true, length: 5, lineStyle: { color: '#514a40', width: 1 } },
                axisLabel: { color: inkSoft, fontFamily: font, fontSize: 10.5, margin: 9, formatter: v => v === 0 ? '' : v },
                splitLine: { show: false }
            },
            yAxis: {
                type: 'value', min: 0, max: 100, interval: 20,
                name: 'UTILIZATION (%)', nameLocation: 'middle', nameGap: 39, nameRotate: 90,
                nameTextStyle: { color: ink, fontFamily: font, fontSize: 10.5, fontWeight: 650 },
                axisLine: { show: true, lineStyle: { color: '#514a40', width: 1.05 } },
                axisTick: { show: true, length: 5, lineStyle: { color: '#514a40', width: 1 } },
                axisLabel: { color: inkSoft, fontFamily: font, fontSize: 10.5, margin: 9 },
                splitLine: { show: false }
            },
            graphic: [
                {
                    type: 'group', right: 18, bottom: 55, silent: true, z: 8,
                    children: [
                        { type: 'rect', shape: { x: 0, y: 0, width: 148, height: 47, r: 4 }, style: { fill: '#d8cdb9', stroke: '#7d7466', lineWidth: 1, shadowBlur: 2, shadowOffsetY: 1, shadowColor: 'rgba(0,0,0,.28)' } },
                        { type: 'circle', shape: { cx: 16, cy: 15, r: 6 }, style: { fill: '#17495d', stroke: '#282b28', lineWidth: 1.2 } },
                        { type: 'text', style: { x: 29, y: 11, text: 'CURRENT (SIZE = SCORE)', fill: ink, font: `9px ${font}` } },
                        { type: 'circle', shape: { cx: 16, cy: 33, r: 6 }, style: { fill: cream, stroke: '#8a8174', lineWidth: 1.1 } },
                        { type: 'text', style: { x: 29, y: 29, text: 'PREVIOUS-MONTH POSITION', fill: ink, font: `9px ${font}` } }
                    ]
                }
            ],
            series: [
                ...tails,
                {
                    name: 'quadrants', type: 'line', silent: true, symbol: 'none', data: [],
                    markLine: {
                        silent: true,
                        symbol: 'none',
                        label: { show: false },
                        lineStyle: { color: rule, type: 'dashed', width: 1, opacity: .82 },
                        data: [{ xAxis: med }, { yAxis: target }]
                    },
                    z: 0
                },
                {
                    name: 'quadrant-labels', type: 'scatter', silent: true, symbolSize: 0, data: quadrantLabels,
                    label: { show: true, color: '#5b554d', fontFamily: font, fontSize: 10, fontWeight: 500, distance: 0 },
                    z: 2
                },
                { name: 'selected-halo', type: 'scatter', silent: true, symbol: 'circle', data: haloData, z: 2 },
                { name: 'bezels', type: 'scatter', silent: true, symbol: 'circle', data: bezelData, z: 3 },
                { name: 'Engineers', type: 'scatter', symbol: 'circle', data: faceData, cursor: 'pointer', z: 4, emphasis: { scale: 1.08 } }
            ]
        };
    }

    function apply(id, points, xMax, utilizationTarget, medianX, selectedName) {
        original(id, points, xMax, utilizationTarget, medianX, selectedName);
        lastArgs.set(id, [points, xMax, utilizationTarget, medianX, selectedName]);
        if (!isRealist()) return;
        const el = document.getElementById(id);
        const chart = el ? echarts.getInstanceByDom(el) : null;
        if (!chart) return;
        chart.setOption(physicalOption(points, xMax, utilizationTarget, medianX, selectedName), true);
        requestAnimationFrame(() => { if (!chart.isDisposed()) chart.resize(); });
    }

    atlas.performanceField = apply;

    const reapply = () => requestAnimationFrame(() => {
        if (!isRealist()) return;
        for (const [id, args] of lastArgs) {
            const el = document.getElementById(id);
            const chart = el ? echarts.getInstanceByDom(el) : null;
            if (chart && !chart.isDisposed()) chart.setOption(physicalOption(...args), true);
        }
    });
    window.addEventListener('epa-theme-changed', reapply);
    window.addEventListener('epa-skin-changed', reapply);
})();