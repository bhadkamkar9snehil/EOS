(() => {
    const cssVar = (name, fallback) => {
        const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
        return value || fallback;
    };

    function weightedStack(id, categories, series, totals, suffix) {
        const chart = window.epaCharts?.ensure(id);
        if (!chart) return;

        chart.clear();
        chart.setOption({
            animationDuration: 650,
            animationEasing: 'cubicOut',
            grid: { left: 160, right: 54, top: 38, bottom: 24, containLabel: false },
            legend: {
                top: 0,
                itemWidth: 11,
                itemHeight: 8,
                itemGap: 18,
                textStyle: { fontSize: 11, color: '#4F5955' }
            },
            tooltip: {
                trigger: 'axis',
                axisPointer: { type: 'shadow' },
                backgroundColor: '#2A3330',
                borderWidth: 0,
                padding: [9, 12],
                textStyle: { color: '#f3f5f8', fontSize: 12, lineHeight: 18 },
                extraCssText: 'box-shadow:0 8px 20px rgba(10,16,28,.28);border-radius:6px;',
                formatter: params => {
                    const index = params[0]?.dataIndex ?? 0;
                    const rows = params
                        .filter(p => p.value != null && Number(p.value) > 0)
                        .map(p => `${p.marker}${p.seriesName}: <b>${Number(p.value).toFixed(1)}${suffix || ''}</b>`);
                    rows.push(`<span style="opacity:.72">Operational score:</span> <b>${Number(totals[index] || 0).toFixed(1)}</b>`);
                    return `<strong>${categories[index]}</strong><br/>${rows.join('<br/>')}`;
                }
            },
            xAxis: {
                type: 'value',
                min: 0,
                max: 100,
                axisLine: { show: false },
                axisTick: { show: false },
                axisLabel: { fontSize: 10, color: cssVar('--ink-soft', '#475569'), formatter: '{value}' },
                splitLine: { lineStyle: { color: cssVar('--chart-grid', '#E4E5E1') } }
            },
            yAxis: {
                type: 'category',
                inverse: true,
                data: categories,
                axisLine: { show: false },
                axisTick: { show: false },
                axisLabel: { fontSize: 10.5, color: cssVar('--chart-ink', '#202623'), width: 146, overflow: 'truncate' }
            },
            series: series.map((item, seriesIndex) => ({
                name: item.name,
                type: 'bar',
                stack: 'operational',
                barWidth: 15,
                emphasis: { focus: 'series' },
                itemStyle: {
                    color: item.color,
                    borderRadius: seriesIndex === 0 ? [3, 0, 0, 3] : seriesIndex === series.length - 1 ? [0, 3, 3, 0] : 0
                },
                label: seriesIndex === series.length - 1 ? {
                    show: true,
                    position: 'right',
                    distance: 7,
                    color: '#202623',
                    fontSize: 10.5,
                    fontWeight: 700,
                    formatter: p => Number(totals[p.dataIndex] || 0).toFixed(1)
                } : { show: false },
                data: item.values
            }))
        }, true);
    }

    window.epaAnalyticsCharts = { weightedStack };
})();
