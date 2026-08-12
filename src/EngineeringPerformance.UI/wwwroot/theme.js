// Theme switcher: persists a light/dark/system choice and toggles the [data-theme] attribute that
// tailwind-input.css's dark palette keys off. All colors live as Tailwind @theme tokens (see
// tailwind-input.css) — this file only ever flips one attribute and asks charts to re-read them.
(() => {
    const KEY = 'epa-theme';

    const apply = mode => {
        const root = document.documentElement;
        if (mode === 'light' || mode === 'dark') root.dataset.theme = mode;
        else delete root.dataset.theme;
    };

    const get = () => localStorage.getItem(KEY) || 'system';

    const set = mode => {
        localStorage.setItem(KEY, mode);
        apply(mode);
        window.epaAtlas?.refreshTheme?.();
        window.epaCharts?.refreshTheme?.();
    };

    window.epaTheme = { get, set };
})();
