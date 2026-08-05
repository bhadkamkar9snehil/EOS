// The configurator palettes are intentionally varied, but analytical series and
// attendance states still need professional chroma. This layer derives muted,
// theme-matched chart and signal colours without changing the palette identity.
(() => {
    const root = document.documentElement;

    const rgb = hex => {
        const value = String(hex || '#000000').replace('#', '');
        return [0, 2, 4].map(index => parseInt(value.slice(index, index + 2), 16));
    };

    const hex = channels =>
        '#' + channels.map(value => Math.max(0, Math.min(255, Math.round(value))).toString(16).padStart(2, '0')).join('').toUpperCase();

    const mix = (first, second, amount = .5) => {
        const a = rgb(first);
        const b = rgb(second);
        return hex(a.map((value, index) => value * (1 - amount) + b[index] * amount));
    };

    const set = (name, value) => root.style.setProperty(name, value);

    function applyRestrainedPalette() {
        const key = window.epaTheme?.get?.();
        const p = window.epaTheme?.palettes?.()?.[key];
        if (!p) return;

        const accent = mix(p.accent, p.text2, .12);
        const info = mix(p.info, p.text2, .16);
        const success = mix(p.success, p.text2, .12);
        const warning = mix(p.warning, p.text2, .12);
        const danger = mix(p.danger, p.text2, .10);
        const approval = mix(p.accent, p.warning, .46);
        const secondary = mix(p.info, p.success, .42);
        const tertiary = mix(p.accent, p.text2, .38);
        const early = mix(p.warning, p.danger, .30);
        const missing = mix(p.danger, p.text2, .20);

        const tokens = {
            '--chart-1': accent,
            '--chart-2': info,
            '--chart-3': success,
            '--chart-4': approval,
            '--chart-5': warning,
            '--chart-6': secondary,
            '--chart-7': danger,
            '--chart-8': tertiary,
            '--chart-operational': accent,
            '--chart-timesheet': info,
            '--chart-approval': approval,
            '--chart-attendance': success,
            '--chart-billable': accent,
            '--chart-nonbillable': warning,
            '--chart-training': approval,
            '--chart-office': success,
            '--chart-heat-low': mix(p.accentBg, p.surface, .38),
            '--chart-heat-high': mix(p.accent, p.headerBg, .30),
            '--signal-punch': info,
            '--signal-punch-soft': mix(p.surface, info, .055),
            '--signal-timesheet': approval,
            '--signal-timesheet-soft': mix(p.surface, approval, .055),
            '--signal-late': warning,
            '--signal-late-soft': mix(p.surface, warning, .055),
            '--signal-early': early,
            '--signal-early-soft': mix(p.surface, early, .055),
            '--signal-short': danger,
            '--signal-short-soft': mix(p.surface, danger, .055),
            '--signal-missing': missing,
            '--signal-missing-soft': mix(p.surface, missing, .055),
            '--serious': early,
            '--serious-soft': mix(p.surface, early, .07)
        };

        Object.entries(tokens).forEach(([name, value]) => set(name, value));

        // The bridge reads current CSS variables, so refresh only after the muted
        // analytical tokens have replaced the high-chroma generated variants.
        window.epaCharts?.refreshTheme?.();
        window.epaAnalyticsCharts?.refreshTheme?.();
    }

    window.addEventListener('epa-theme-changed', () => queueMicrotask(applyRestrainedPalette));
    applyRestrainedPalette();
})();
