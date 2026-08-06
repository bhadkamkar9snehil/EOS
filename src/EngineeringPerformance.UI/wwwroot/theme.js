(() => {
    const storageKey = "epa-theme";
    const palettes = {
        // ---- Neutral (light) ----
        'graphite':{name:'Graphite',desc:'Executive neutral',group:'neutral',ink:'#0f172a',text:'#1e293b',text2:'#334155',muted:'#64748b',border:'#dbe2ea',bg:'#f4f7fb',surface:'#ffffff',surface2:'#f8fafc',accent:'#2563eb',accentBg:'#eff6ff',headerBg:'#0f172a',headerText:'#f8fafc',success:'#15803d',successBg:'#f0fdf4',warning:'#b45309',warningBg:'#fffbeb',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#0369a1',infoBg:'#e0f2fe'},
        'ledger':{name:'Ledger',desc:'Fintech calm',group:'neutral',ink:'#101828',text:'#1d2939',text2:'#344054',muted:'#667085',border:'#d0d5dd',bg:'#f7f8fa',surface:'#ffffff',surface2:'#f9fafb',accent:'#175cd3',accentBg:'#eff8ff',headerBg:'#101828',headerText:'#f8fafc',success:'#067647',successBg:'#ecfdf3',warning:'#b54708',warningBg:'#fffaeb',danger:'#b42318',dangerBg:'#fef3f2',info:'#175cd3',infoBg:'#eff8ff'},
        'sterling':{name:'Sterling',desc:'Steel neutral',group:'neutral',ink:'#111827',text:'#1f2937',text2:'#4b5563',muted:'#6b7280',border:'#d1d5db',bg:'#f3f4f6',surface:'#ffffff',surface2:'#f8fafc',accent:'#0f766e',accentBg:'#def7ec',headerBg:'#1f2937',headerText:'#f9fafb',success:'#0f766e',successBg:'#ecfdf5',warning:'#c2410c',warningBg:'#fff7ed',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#0369a1',infoBg:'#e0f2fe'},
        'canvas':{name:'Canvas',desc:'Soft neutral',group:'neutral',ink:'#111827',text:'#1f2937',text2:'#4b5563',muted:'#6b7280',border:'#e5e7eb',bg:'#f9fafb',surface:'#ffffff',surface2:'#f8fafc',accent:'#475569',accentBg:'#f1f5f9',headerBg:'#1f2937',headerText:'#f9fafb',success:'#15803d',successBg:'#f0fdf4',warning:'#b45309',warningBg:'#fffbeb',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#475569',infoBg:'#f1f5f9'},
        'quartz':{name:'Quartz',desc:'Monochrome crisp',group:'neutral',ink:'#0a0a0a',text:'#171717',text2:'#404040',muted:'#737373',border:'#d4d4d4',bg:'#f5f5f5',surface:'#ffffff',surface2:'#fafafa',accent:'#262626',accentBg:'#f5f5f5',headerBg:'#171717',headerText:'#fafafa',success:'#15803d',successBg:'#f0fdf4',warning:'#b45309',warningBg:'#fffbeb',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#262626',infoBg:'#f5f5f5'},
        'sandstone':{name:'Sandstone',desc:'ERP warm neutral',group:'neutral',ink:'#1f2937',text:'#374151',text2:'#4b5563',muted:'#6b7280',border:'#e5e7eb',bg:'#faf7f2',surface:'#fffdf9',surface2:'#f7f2ea',accent:'#92400e',accentBg:'#fffbeb',headerBg:'#292524',headerText:'#fafaf9',success:'#15803d',successBg:'#f0fdf4',warning:'#b45309',warningBg:'#fffbeb',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#92400e',infoBg:'#ffedd5'},

        // ---- Dark (inspired by well-known editor themes, non-blue accents) ----
        'dracula':{name:'Dracula',desc:'Purple & pink classic',group:'dark',ink:'#f8f8f2',text:'#e6e6e0',text2:'#c2c4d6',muted:'#8a8ca6',border:'#44475a',bg:'#282a36',surface:'#343746',surface2:'#2f3241',accent:'#bd93f9',accentBg:'#3b3465',headerBg:'#1e1f29',headerText:'#f8f8f2',success:'#50fa7b',successBg:'#1f3327',warning:'#f1fa8c',warningBg:'#3a3820',danger:'#ff5555',dangerBg:'#3a1f22',info:'#ff79c6',infoBg:'#3a2331'},
        'gruvbox':{name:'Gruvbox',desc:'Retro warm contrast',group:'dark',ink:'#ebdbb2',text:'#d5c4a1',text2:'#bdae93',muted:'#928374',border:'#504945',bg:'#282828',surface:'#32302f',surface2:'#2d2b2a',accent:'#fe8019',accentBg:'#3c2f1e',headerBg:'#1d2021',headerText:'#fbf1c7',success:'#8ec07c',successBg:'#1e2e28',warning:'#fabd2f',warningBg:'#3a331a',danger:'#fb4934',dangerBg:'#3a201d',info:'#83a598',infoBg:'#1c2a2c'},
        'monokai':{name:'Monokai',desc:'Editor pink & lime',group:'dark',ink:'#f8f8f2',text:'#e0e0d4',text2:'#c2c3b8',muted:'#90918a',border:'#49493f',bg:'#272822',surface:'#33342c',surface2:'#2d2e26',accent:'#f92672',accentBg:'#3d1f2b',headerBg:'#1e1f1a',headerText:'#f8f8f2',success:'#a6e22e',successBg:'#2e3a1a',warning:'#e6db74',warningBg:'#3a3820',danger:'#fd5c63',dangerBg:'#3a1f20',info:'#ae81ff',infoBg:'#2c2440'},
        'everforest':{name:'Everforest',desc:'Soft forest green',group:'dark',ink:'#d3c6aa',text:'#c6b89c',text2:'#9da9a0',muted:'#7a8478',border:'#4a555b',bg:'#2d353b',surface:'#343f44',surface2:'#333c41',accent:'#a7c080',accentBg:'#333d2e',headerBg:'#232a2e',headerText:'#d3c6aa',success:'#83c092',successBg:'#233029',warning:'#dbbc7f',warningBg:'#3a3423',danger:'#e67e80',dangerBg:'#3a2323',info:'#7fbbb3',infoBg:'#1f2c2c'},
        'solarized-dark':{name:'Solarized Dark',desc:'Yellow accent, no blue',group:'dark',ink:'#eee8d5',text:'#c8cdc7',text2:'#93a1a1',muted:'#657b83',border:'#0a4552',bg:'#002b36',surface:'#073642',surface2:'#04303b',accent:'#b58900',accentBg:'#35301a',headerBg:'#00212b',headerText:'#eee8d5',success:'#859900',successBg:'#2b3315',warning:'#cb4b16',warningBg:'#3a2213',danger:'#dc322f',dangerBg:'#3a1616',info:'#d33682',infoBg:'#3a1d2c'}
    };

    const normalize = value => Object.prototype.hasOwnProperty.call(palettes, value) ? value : "graphite";

    const hexToRgb = value => {
        const hex = String(value).replace("#", "");
        return [0, 2, 4].map(index => parseInt(hex.slice(index, index + 2), 16) / 255);
    };

    const rgbToHex = values =>
        "#" + values.map(value => Math.max(0, Math.min(255, Math.round(value * 255))).toString(16).padStart(2, "0")).join("").toUpperCase();

    const rgbToHsl = ([r, g, b]) => {
        const max = Math.max(r, g, b);
        const min = Math.min(r, g, b);
        let h = 0;
        let s = 0;
        const l = (max + min) / 2;
        const delta = max - min;

        if (delta !== 0) {
            s = delta / (1 - Math.abs(2 * l - 1));
            if (max === r) h = 60 * (((g - b) / delta) % 6);
            else if (max === g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
        }

        return [(h + 360) % 360, s, l];
    };

    const hslToRgb = ([h, s, l]) => {
        const c = (1 - Math.abs(2 * l - 1)) * s;
        const x = c * (1 - Math.abs(((h / 60) % 2) - 1));
        const m = l - c / 2;
        let rgb;

        if (h < 60) rgb = [c, x, 0];
        else if (h < 120) rgb = [x, c, 0];
        else if (h < 180) rgb = [0, c, x];
        else if (h < 240) rgb = [0, x, c];
        else if (h < 300) rgb = [x, 0, c];
        else rgb = [c, 0, x];

        return rgb.map(value => value + m);
    };

    const mix = (first, second, amount = .5) => {
        const a = hexToRgb(first);
        const b = hexToRgb(second);
        return rgbToHex(a.map((value, index) => value * (1 - amount) + b[index] * amount));
    };

    const darken = (hex, amount) => {
        const [h, s, l] = rgbToHsl(hexToRgb(hex));
        return rgbToHex(hslToRgb([h, s, Math.max(0, l - amount)]));
    };

    // Picks readable ink for text painted directly on a solid fill (status chips, badges).
    // A fixed white was assumed everywhere this pattern is used, which fails hard on themes
    // whose status color is itself light — Gruvbox/Solarized's warning is a bright yellow, and
    // white-on-bright-yellow is close to unreadable. Real luminance, not just lightness, decides
    // white vs near-black the way any accessible-contrast check would.
    const inkFor = hex => {
        const [r, g, b] = hexToRgb(hex);
        const luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        return luminance > 0.6 ? "#141200" : "#FFFFFF";
    };

    // A single-hue sequential ramp (dataviz convention: one hue, light -> dark for
    // magnitude) built off the accent's own hue/saturation. On light canvases low
    // values sit near-white and high values go deep and saturated; on dark canvases
    // the direction flips so low values blend toward the (dark) canvas and high
    // values pop bright — either way the low/high ends stay perceptually far apart,
    // which a flat accent-to-darkened-accent pair could not guarantee.
    const heatScale = (hex, isDark) => {
        const [h, s] = rgbToHsl(hexToRgb(hex));
        const sat = Math.min(1, Math.max(s, .55));
        const lights = isDark ? [.15, .28, .42, .58, .76] : [.94, .80, .63, .47, .30];
        return lights.map(l => rgbToHex(hslToRgb([h, sat, l])));
    };

    const set = (name, value) => document.documentElement.style.setProperty(name, value);

    const installTokens = palette => {
        const isDark = palette.group === "dark";
        const heat = heatScale(palette.accent, isDark);

        // Analytical/chart colors are deliberately muted relative to the raw palette — full-chroma
        // brand colors read as loud/toy-like across eight simultaneous chart series. Professional
        // chroma, still theme-matched, computed once (this used to be a second file that ran after
        // this one and overwrote these same tokens on every theme change).
        const restrainedAccent = mix(palette.accent, palette.text2, .12);
        const restrainedInfo = mix(palette.info, palette.text2, .16);
        const restrainedSuccess = mix(palette.success, palette.text2, .12);
        const restrainedWarning = mix(palette.warning, palette.text2, .12);
        const restrainedDanger = mix(palette.danger, palette.text2, .10);
        const restrainedApproval = mix(palette.accent, palette.warning, .46);
        const restrainedSecondary = mix(palette.info, palette.success, .42);
        const restrainedTertiary = mix(palette.accent, palette.text2, .38);
        const restrainedEarly = mix(palette.warning, palette.danger, .30);
        const restrainedMissing = mix(palette.danger, palette.text2, .20);

        const surfaceMuted = mix(palette.bg, palette.surface2, .5);
        const surfaceSoft = mix(palette.surface2, palette.surface, .35);
        const cardSurface = mix(palette.surface2, palette.surface, .70);

        const tokens = {
            "--shadow-resting": isDark ? "0 1px 2px rgba(0, 0, 0, .5)" : "0 1px 2px rgba(32, 38, 35, .05)",
            "--shadow-raised": isDark
                ? "0 6px 20px rgba(0, 0, 0, .55), 0 1px 0 rgba(255, 255, 255, .05) inset"
                : "0 4px 14px rgba(32, 38, 35, .10)",
            "--shadow-drawer": isDark ? "0 24px 60px rgba(0, 0, 0, .65)" : "0 24px 60px rgba(20, 26, 23, .28)",
            "--shadow-nav": isDark ? "1px 0 0 rgba(255, 255, 255, .04) inset" : "none",
            "color-scheme": isDark ? "dark" : "light",
            // A real surface ladder, not four names pointing at one value. Each theme still only
            // authors three raw tones (bg / surface2 / surface); the steps between them are
            // interpolated so canvas -> muted -> surface -> soft -> card -> raised are each a
            // measurable step apart instead of collapsing into "canvas" and "everything else."
            "--canvas": palette.bg,
            "--surface-muted": surfaceMuted,
            "--surface": palette.surface2,
            "--surface-soft": surfaceSoft,
            "--card-surface": cardSurface,
            "--surface-raised": palette.surface,
            "--ink": palette.ink,
            "--ink-soft": palette.text2,
            "--muted": palette.muted,
            "--line": palette.border,
            "--line-soft": mix(palette.border, palette.surface, .58),
            "--line-strong": mix(palette.border, palette.text2, .30),
            "--primary": palette.accent,
            "--primary-hover": darken(palette.accent, .08),
            "--primary-ink": inkFor(palette.accent),
            /* On light themes headerBg (a dark navy/ink) doubles as legible text on soft
               backgrounds. On dark themes headerBg is near-black, so that pairing would be
               invisible against the equally-dark soft surfaces — use a lightened accent instead. */
            "--primary-strong": isDark ? darken(palette.accent, -.16) : palette.headerBg,
            "--primary-soft": palette.accentBg,
            "--secondary": palette.info,
            "--secondary-hover": darken(palette.info, .08),
            "--secondary-dark": isDark ? darken(palette.info, -.08) : darken(palette.info, .14),
            "--secondary-soft": palette.infoBg,
            "--secondary-ink": inkFor(isDark ? darken(palette.info, -.08) : darken(palette.info, .14)),
            "--good": palette.success,
            "--good-soft": palette.successBg,
            "--good-ink": inkFor(palette.success),
            "--warn": palette.warning,
            "--warn-soft": palette.warningBg,
            "--warn-ink": inkFor(palette.warning),
            "--serious": restrainedEarly,
            "--serious-soft": mix(palette.surface, restrainedEarly, .10),
            "--serious-ink": inkFor(restrainedEarly),
            "--critical": palette.danger,
            "--critical-soft": palette.dangerBg,
            "--critical-ink": inkFor(palette.danger),
            "--table-header": palette.headerBg,
            "--table-header-text": palette.headerText,
            "--table-header-line": mix(palette.headerBg, palette.headerText, .22),
            "--table-row": palette.surface,
            "--table-row-alt": surfaceSoft,
            "--table-row-hover": palette.accentBg,
            "--nav-bg": palette.headerBg,
            "--nav-line": darken(palette.headerBg, .05),
            "--nav-text": mix(palette.headerBg, palette.headerText, .78),
            "--nav-text-strong": palette.headerText,
            "--nav-muted": mix(palette.headerBg, palette.headerText, .55),
            "--nav-icon": mix(palette.headerBg, palette.headerText, .65),
            "--nav-hover": `color-mix(in srgb, ${palette.accent} 18%, transparent)`,
            "--nav-selected": `color-mix(in srgb, ${palette.accent} 34%, transparent)`,
            "--focus-soft": `color-mix(in srgb, ${palette.accent} 22%, transparent)`,
            // Muted, theme-matched analytical colors — this was formerly a *separate* file
            // (theme-restraint.js) that ran after this one on every theme change and silently
            // overwrote these exact tokens with its own formula, plus triggered a second full
            // chart repaint. There is now exactly one place these are computed.
            "--chart-1": restrainedAccent,
            "--chart-2": restrainedInfo,
            "--chart-3": restrainedSuccess,
            "--chart-4": restrainedApproval,
            "--chart-5": restrainedWarning,
            "--chart-6": restrainedSecondary,
            "--chart-7": restrainedDanger,
            "--chart-8": restrainedTertiary,
            "--chart-operational": restrainedAccent,
            "--chart-timesheet": restrainedInfo,
            "--chart-approval": restrainedApproval,
            "--chart-attendance": restrainedSuccess,
            "--chart-billable": restrainedAccent,
            "--chart-nonbillable": restrainedWarning,
            "--chart-training": restrainedApproval,
            "--chart-office": restrainedSuccess,
            "--chart-grid": mix(palette.border, palette.surface, .58),
            "--chart-axis": mix(palette.border, palette.text2, .30),
            "--chart-muted": palette.muted,
            "--chart-ink": palette.ink,
            "--chart-tooltip": palette.headerBg,
            "--chart-tooltip-text": palette.headerText,
            "--chart-heat-1": heat[0],
            "--chart-heat-2": heat[1],
            "--chart-heat-3": heat[2],
            "--chart-heat-4": heat[3],
            "--chart-heat-5": heat[4],
            "--chart-heat-low": heat[0],
            "--chart-heat-high": heat[4],
            "--signal-punch": restrainedInfo,
            "--signal-punch-soft": mix(palette.surface, restrainedInfo, .07),
            "--signal-timesheet": restrainedApproval,
            "--signal-timesheet-soft": mix(palette.surface, restrainedApproval, .07),
            "--signal-late": restrainedWarning,
            "--signal-late-soft": mix(palette.surface, restrainedWarning, .07),
            "--signal-early": restrainedEarly,
            "--signal-early-soft": mix(palette.surface, restrainedEarly, .07),
            "--signal-short": restrainedDanger,
            "--signal-short-soft": mix(palette.surface, restrainedDanger, .07),
            "--signal-missing": restrainedMissing,
            "--signal-missing-soft": mix(palette.surface, restrainedMissing, .07)
        };

        Object.entries(tokens).forEach(([name, value]) => set(name, value));
    };

    const cssValue = (name, fallback) => {
        const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
        return value || fallback;
    };

    const chartPalette = () => ({
        series: Array.from({ length: 8 }, (_, index) => cssValue(`--chart-${index + 1}`, "#2563EB")),
        operational: cssValue("--chart-operational", "#2563EB"),
        timesheet: cssValue("--chart-timesheet", "#0891B2"),
        approval: cssValue("--chart-approval", "#7C3AED"),
        attendance: cssValue("--chart-attendance", "#15803D"),
        billable: cssValue("--chart-billable", "#2563EB"),
        nonBillable: cssValue("--chart-nonbillable", "#B45309"),
        training: cssValue("--chart-training", "#7C3AED"),
        office: cssValue("--chart-office", "#15803D"),
        good: cssValue("--good", "#15803D"),
        warning: cssValue("--warn", "#B45309"),
        serious: cssValue("--serious", "#C2410C"),
        critical: cssValue("--critical", "#B91C1C"),
        grid: cssValue("--chart-grid", "#E5E7EB"),
        axis: cssValue("--chart-axis", "#CBD5E1"),
        muted: cssValue("--chart-muted", "#64748B"),
        inkSoft: cssValue("--ink-soft", "#475569"),
        ink: cssValue("--chart-ink", "#0F172A"),
        tooltip: cssValue("--chart-tooltip", "#0F172A"),
        tooltipText: cssValue("--chart-tooltip-text", "#F8FAFC"),
        heatLow: cssValue("--chart-heat-low", "#EFF6FF"),
        heatHigh: cssValue("--chart-heat-high", "#1D4ED8"),
        heatScale: Array.from({ length: 5 }, (_, index) => cssValue(`--chart-heat-${index + 1}`, "#2563EB")),
        surface: cssValue("--surface-raised", "#FFFFFF")
    });

    const apply = value => {
        const theme = normalize(value);
        const palette = palettes[theme];
        document.documentElement.dataset.theme = theme;
        installTokens(palette);

        try { localStorage.setItem(storageKey, theme); } catch { }

        // Tokens are installed; notify once. theme-chart-bridge listens for this and repaints
        // every live chart itself — calling refreshTheme directly here as well (the previous
        // behavior) repainted every chart a second time on every theme switch for no reason.
        window.dispatchEvent(new CustomEvent("epa-theme-changed", {
            detail: { theme, palette: chartPalette() }
        }));
        return theme;
    };

    window.epaTheme = {
        get: () => normalize(document.documentElement.dataset.theme || localStorage.getItem(storageKey)),
        apply,
        chartPalette,
        palettes: () => palettes,
        initialize: () => apply(localStorage.getItem(storageKey))
    };

    window.epaTheme.initialize();
})();
