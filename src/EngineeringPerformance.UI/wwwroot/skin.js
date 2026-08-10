(() => {
    const key = 'epa-ui-skin';
    const allowed = ['minimal', 'realist'];
    const normalize = value => allowed.includes(value) ? value : 'minimal';
    const safeGet = () => { try { return normalize(localStorage.getItem(key) || 'minimal'); } catch { return 'minimal'; } };
    const safeSet = value => { try { localStorage.setItem(key, value); } catch { } };

    const realistChartTokens = {
        '--chart-operational':'#0B536F',
        '--chart-attendance':'#0B536F',
        '--chart-timesheet':'#0B536F',
        '--chart-approval':'#0B536F',
        '--chart-billable':'#0B536F',
        '--chart-nonbillable':'#ED6B16',
        '--chart-training':'#C47B12',
        '--chart-office':'#0B536F',
        '--chart-punch':'#0B536F',
        '--chart-underutilized':'#607785',
        '--chart-grid':'#C8BDAA',
        '--chart-axis':'#857D72',
        '--chart-muted':'#6C675E',
        '--chart-ink':'#171B19',
        '--chart-tooltip':'#13232D',
        '--on-chart-tooltip':'#F6EAD5',
        '--chart-surface':'#EFE5D4',
        '--chart-1':'#0B536F',
        '--chart-2':'#ED6B16',
        '--chart-3':'#4C8C3C',
        '--chart-4':'#A82016',
        '--chart-5':'#C47B12',
        '--chart-6':'#376D82',
        '--chart-7':'#8B5A31',
        '--chart-8':'#667A55'
    };

    function enforceProductPalette() {
        try {
            if (window.epaTheme?.get?.() !== 'graphite') window.epaTheme?.apply?.('graphite');
        } catch { }
    }

    function applySkinTokens(skin) {
        const style = document.documentElement.style;
        if (skin === 'realist') {
            Object.entries(realistChartTokens).forEach(([name, value]) => style.setProperty(name, value));
            style.setProperty('--atlas-orange', '#ED6B16');
        } else {
            Object.keys(realistChartTokens).forEach(name => style.removeProperty(name));
            style.removeProperty('--atlas-orange');
            try { window.epaTheme?.apply?.('graphite'); } catch { }
        }
    }

    function apply(value, announce = true) {
        const next = normalize(value);
        safeSet(next);
        document.documentElement.dataset.skin = next;
        enforceProductPalette();
        applySkinTokens(next);
        if (announce) {
            window.dispatchEvent(new CustomEvent('epa-skin-changed', { detail: { skin: next } }));
            window.epaAtlas?.refreshTheme?.();
            window.epaCharts?.refreshTheme?.();
        }
        return next;
    }

    window.epaSkin = {
        get: () => normalize(document.documentElement.dataset.skin || safeGet()),
        apply,
        initialize: () => apply(safeGet(), false)
    };

    window.epaSkin.initialize();
})();
