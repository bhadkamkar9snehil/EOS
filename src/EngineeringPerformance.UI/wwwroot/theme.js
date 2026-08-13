// Theme switcher. Persists the user's choice and resolves it to a concrete theme name on the
// root element; tailwind-input.css then keys every palette off [data-theme="..."]. All colours
// live as Tailwind @theme tokens — this file only ever sets one attribute and asks the charts to
// re-read them.
//
// Why "system" is RESOLVED rather than left as an unset attribute:
//   1. It removes the need for a duplicate prefers-color-scheme copy of every dark palette in
//      CSS (that duplication was ~100 lines maintained twice, and would have multiplied with
//      each theme added).
//   2. It fixes a real bug. ECharts reads its colours once, via getComputedStyle, at draw time.
//      With a media-query-driven theme, an OS light/dark switch repainted the CSS but left every
//      chart rendering its previous palette — stranded against the new background. Resolving here
//      means the same code path that changes the theme also calls refreshTheme(), so the charts
//      always repaint with it.
(() => {
    const KEY = 'epa-theme';
    const DEFAULT = 'system';

    // Keep in sync with the :root[data-theme="..."] blocks in tailwind-input.css.
    const THEMES = ['light', 'slate', 'dark', 'midnight', 'contrast'];
    // Which concrete theme "system" maps to. Deliberately the two default palettes, not the
    // specialised midnight/contrast ones, which are always an explicit opt-in.
    const SYSTEM_DARK = 'dark';
    const SYSTEM_LIGHT = 'light';

    const media = window.matchMedia?.('(prefers-color-scheme: dark)');

    const resolve = mode => mode === 'system'
        ? (media?.matches ? SYSTEM_DARK : SYSTEM_LIGHT)
        : (THEMES.includes(mode) ? mode : SYSTEM_LIGHT);

    const repaintCharts = () => {
        // Charts cache their palette at draw time, so they must be told to re-read the tokens.
        window.epaAtlas?.refreshTheme?.();
        window.epaCharts?.refreshTheme?.();
    };

    const apply = mode => {
        document.documentElement.dataset.theme = resolve(mode);
        repaintCharts();
    };

    const get = () => {
        try { return localStorage.getItem(KEY) || DEFAULT; }
        catch { return DEFAULT; }
    };

    const set = mode => {
        const next = mode === 'system' || THEMES.includes(mode) ? mode : DEFAULT;
        try { localStorage.setItem(KEY, next); } catch { /* private mode: apply without persisting */ }
        apply(next);
    };

    // Track the OS while the user is on "system" — without this, an OS theme change mid-session
    // would leave the app on whichever palette it happened to start with.
    media?.addEventListener?.('change', () => { if (get() === 'system') apply('system'); });

    // The inline boot script in index.html sets the attribute pre-paint to avoid a flash; this
    // re-applies once the real theme list is known (e.g. a stored value no longer in THEMES).
    apply(get());

    window.epaTheme = { get, set, list: () => THEMES.slice(), resolved: () => resolve(get()) };
})();
