(() => {
    const storageKey = "epa-theme";
    const allowed = new Set([
        "clay-sage",
        "harbor-blue",
        "forest-ledger",
        "graphite-copper",
        "alpine-ink"
    ]);

    const normalize = value => allowed.has(value) ? value : "clay-sage";

    const apply = value => {
        const theme = normalize(value);
        document.documentElement.dataset.theme = theme;
        try { localStorage.setItem(storageKey, theme); } catch { }
        window.dispatchEvent(new CustomEvent("epa-theme-changed", { detail: theme }));
        return theme;
    };

    window.epaTheme = {
        get: () => normalize(document.documentElement.dataset.theme || localStorage.getItem(storageKey)),
        apply,
        initialize: () => apply(localStorage.getItem(storageKey))
    };

    window.epaTheme.initialize();
})();
