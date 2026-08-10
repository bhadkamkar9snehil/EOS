(() => {
    const key = 'epa-ui-skin';
    const allowed = ['minimal', 'realist'];
    const normalize = value => allowed.includes(value) ? value : 'minimal';
    const safeGet = () => { try { return normalize(localStorage.getItem(key) || 'minimal'); } catch { return 'minimal'; } };
    const safeSet = value => { try { localStorage.setItem(key, value); } catch { } };

    function enforceProductPalette() {
        try {
            if (window.epaTheme?.get?.() !== 'graphite') window.epaTheme?.apply?.('graphite');
        } catch { }
    }

    function apply(value, announce = true) {
        const next = normalize(value);
        safeSet(next);
        document.documentElement.dataset.skin = next;
        enforceProductPalette();
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
