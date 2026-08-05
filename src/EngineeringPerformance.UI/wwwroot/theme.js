(() => {
    const storageKey = "epa-theme";
    const palettes = {
        'graphite':{name:'Graphite',desc:'Executive neutral',group:'neutral',ink:'#0f172a',text:'#1e293b',text2:'#334155',muted:'#64748b',border:'#dbe2ea',bg:'#f4f7fb',surface:'#ffffff',surface2:'#f8fafc',accent:'#2563eb',accentBg:'#eff6ff',headerBg:'#0f172a',headerText:'#f8fafc',success:'#15803d',successBg:'#f0fdf4',warning:'#b45309',warningBg:'#fffbeb',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#0369a1',infoBg:'#e0f2fe'},
        'ledger':{name:'Ledger',desc:'Fintech calm',group:'neutral',ink:'#101828',text:'#1d2939',text2:'#344054',muted:'#667085',border:'#d0d5dd',bg:'#f7f8fa',surface:'#ffffff',surface2:'#f9fafb',accent:'#175cd3',accentBg:'#eff8ff',headerBg:'#101828',headerText:'#f8fafc',success:'#067647',successBg:'#ecfdf3',warning:'#b54708',warningBg:'#fffaeb',danger:'#b42318',dangerBg:'#fef3f2',info:'#175cd3',infoBg:'#eff8ff'},
        'harbor':{name:'Harbor',desc:'Teal operations',group:'blue',ink:'#111827',text:'#1f2937',text2:'#475569',muted:'#64748b',border:'#d7dee7',bg:'#eef3f8',surface:'#ffffff',surface2:'#f7fafc',accent:'#0f766e',accentBg:'#ccfbf1',headerBg:'#102a43',headerText:'#eff6ff',success:'#15803d',successBg:'#f0fdf4',warning:'#b45309',warningBg:'#fffbeb',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#0f766e',infoBg:'#ccfbf1'},
        'alloy':{name:'Alloy',desc:'Indigo product',group:'expressive',ink:'#16181d',text:'#23262d',text2:'#4b5563',muted:'#6b7280',border:'#d8dde6',bg:'#f3f4f6',surface:'#ffffff',surface2:'#f8fafc',accent:'#4f46e5',accentBg:'#eef2ff',headerBg:'#111827',headerText:'#f9fafb',success:'#166534',successBg:'#f0fdf4',warning:'#b45309',warningBg:'#fffbeb',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#4338ca',infoBg:'#eef2ff'},
        'meridian':{name:'Meridian',desc:'Corporate blue',group:'blue',ink:'#0f172a',text:'#1e293b',text2:'#475569',muted:'#64748b',border:'#d5dde8',bg:'#f2f6fb',surface:'#ffffff',surface2:'#f7f9fc',accent:'#1d4ed8',accentBg:'#dbeafe',headerBg:'#10213a',headerText:'#eff6ff',success:'#15803d',successBg:'#f0fdf4',warning:'#b45309',warningBg:'#fffbeb',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#1d4ed8',infoBg:'#dbeafe'},
        'sterling':{name:'Sterling',desc:'Steel neutral',group:'neutral',ink:'#111827',text:'#1f2937',text2:'#4b5563',muted:'#6b7280',border:'#d1d5db',bg:'#f3f4f6',surface:'#ffffff',surface2:'#f8fafc',accent:'#0f766e',accentBg:'#def7ec',headerBg:'#1f2937',headerText:'#f9fafb',success:'#0f766e',successBg:'#ecfdf5',warning:'#c2410c',warningBg:'#fff7ed',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#0369a1',infoBg:'#e0f2fe'},
        'pine':{name:'Pine',desc:'Industrial green',group:'green',ink:'#111827',text:'#1f2937',text2:'#475569',muted:'#64748b',border:'#d8e2dc',bg:'#f3f7f4',surface:'#ffffff',surface2:'#f7faf7',accent:'#166534',accentBg:'#dcfce7',headerBg:'#132a1d',headerText:'#ecfdf5',success:'#15803d',successBg:'#f0fdf4',warning:'#a16207',warningBg:'#fefce8',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#166534',infoBg:'#dcfce7'},
        'ember':{name:'Ember',desc:'Warm enterprise',group:'warm',ink:'#1c1917',text:'#292524',text2:'#57534e',muted:'#78716c',border:'#e7e5e4',bg:'#f8f5f2',surface:'#ffffff',surface2:'#fafaf9',accent:'#c2410c',accentBg:'#fff7ed',headerBg:'#292524',headerText:'#fafaf9',success:'#15803d',successBg:'#f0fdf4',warning:'#b45309',warningBg:'#fffbeb',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#9a3412',infoBg:'#ffedd5'},
        'atlas':{name:'Atlas',desc:'SaaS cobalt',group:'blue',ink:'#0f172a',text:'#1e293b',text2:'#334155',muted:'#64748b',border:'#dde6f2',bg:'#f4f7fd',surface:'#ffffff',surface2:'#f8fbff',accent:'#2563eb',accentBg:'#eff6ff',headerBg:'#132239',headerText:'#eff6ff',success:'#15803d',successBg:'#f0fdf4',warning:'#b45309',warningBg:'#fffbeb',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#0369a1',infoBg:'#e0f2fe'},
        'canvas':{name:'Canvas',desc:'Soft neutral',group:'neutral',ink:'#111827',text:'#1f2937',text2:'#4b5563',muted:'#6b7280',border:'#e5e7eb',bg:'#f9fafb',surface:'#ffffff',surface2:'#f8fafc',accent:'#475569',accentBg:'#f1f5f9',headerBg:'#1f2937',headerText:'#f9fafb',success:'#15803d',successBg:'#f0fdf4',warning:'#b45309',warningBg:'#fffbeb',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#475569',infoBg:'#f1f5f9'},
        'quartz':{name:'Quartz',desc:'Monochrome crisp',group:'neutral',ink:'#0a0a0a',text:'#171717',text2:'#404040',muted:'#737373',border:'#d4d4d4',bg:'#f5f5f5',surface:'#ffffff',surface2:'#fafafa',accent:'#262626',accentBg:'#f5f5f5',headerBg:'#171717',headerText:'#fafafa',success:'#15803d',successBg:'#f0fdf4',warning:'#b45309',warningBg:'#fffbeb',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#262626',infoBg:'#f5f5f5'},
        'sandstone':{name:'Sandstone',desc:'ERP warm neutral',group:'warm',ink:'#1f2937',text:'#374151',text2:'#4b5563',muted:'#6b7280',border:'#e5e7eb',bg:'#faf7f2',surface:'#fffdf9',surface2:'#f7f2ea',accent:'#92400e',accentBg:'#fffbeb',headerBg:'#292524',headerText:'#fafaf9',success:'#15803d',successBg:'#f0fdf4',warning:'#b45309',warningBg:'#fffbeb',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#92400e',infoBg:'#ffedd5'},
        'aurora':{name:'Aurora',desc:'Bright product blue',group:'blue',ink:'#10213a',text:'#1f3350',text2:'#49627e',muted:'#6c7f95',border:'#d7e3f2',bg:'#f3f8ff',surface:'#ffffff',surface2:'#f7fbff',accent:'#0f6cbd',accentBg:'#e0f2fe',headerBg:'#153a66',headerText:'#eff6ff',success:'#15803d',successBg:'#f0fdf4',warning:'#b45309',warningBg:'#fffbeb',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#0f6cbd',infoBg:'#e0f2fe'},
        'clover':{name:'Clover',desc:'Fresh SaaS green',group:'green',ink:'#10261d',text:'#1e3a2f',text2:'#4b635a',muted:'#6b7d75',border:'#d5e5dd',bg:'#f3fbf6',surface:'#ffffff',surface2:'#f7fcf8',accent:'#15803d',accentBg:'#dcfce7',headerBg:'#123524',headerText:'#ecfdf5',success:'#15803d',successBg:'#f0fdf4',warning:'#a16207',warningBg:'#fefce8',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#0f766e',infoBg:'#ccfbf1'},
        'fjord':{name:'Fjord',desc:'Slate teal blend',group:'blue',ink:'#0f172a',text:'#23364a',text2:'#476072',muted:'#64748b',border:'#d8e1ea',bg:'#f2f7fa',surface:'#ffffff',surface2:'#f8fbfd',accent:'#0f766e',accentBg:'#ccfbf1',headerBg:'#1f3a4a',headerText:'#f0fdfa',success:'#15803d',successBg:'#f0fdf4',warning:'#b45309',warningBg:'#fffbeb',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#0f766e',infoBg:'#ccfbf1'},
        'blossom':{name:'Blossom',desc:'Rose product suite',group:'expressive',ink:'#2a1722',text:'#44263a',text2:'#6b4b5f',muted:'#8a7284',border:'#eed7e4',bg:'#fff6fb',surface:'#ffffff',surface2:'#fff9fc',accent:'#db2777',accentBg:'#fce7f3',headerBg:'#4a1235',headerText:'#fff1f8',success:'#15803d',successBg:'#f0fdf4',warning:'#c2410c',warningBg:'#fff7ed',danger:'#be123c',dangerBg:'#fff1f2',info:'#be185d',infoBg:'#fce7f3'},
        'dune':{name:'Dune',desc:'Sand and bronze',group:'warm',ink:'#2b2115',text:'#463424',text2:'#6b5540',muted:'#8b735d',border:'#eadfcf',bg:'#fcf8f1',surface:'#fffdfa',surface2:'#faf4ea',accent:'#b7791f',accentBg:'#fef3c7',headerBg:'#53331a',headerText:'#fffaf0',success:'#2f855a',successBg:'#f0fdf4',warning:'#b45309',warningBg:'#fffbeb',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#b7791f',infoBg:'#fef3c7'},
        'glacier':{name:'Glacier',desc:'Icy analytics blue',group:'blue',ink:'#10233d',text:'#1d3557',text2:'#4a6585',muted:'#6b7b93',border:'#d8e5f2',bg:'#f5faff',surface:'#ffffff',surface2:'#f8fbff',accent:'#0284c7',accentBg:'#e0f2fe',headerBg:'#12375b',headerText:'#eff6ff',success:'#15803d',successBg:'#f0fdf4',warning:'#b45309',warningBg:'#fffbeb',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#0284c7',infoBg:'#e0f2fe'},
        'orchard':{name:'Orchard',desc:'Olive and apricot',group:'green',ink:'#22231a',text:'#3a3b2b',text2:'#5f604b',muted:'#7b7d66',border:'#e3e5d6',bg:'#f8f9f2',surface:'#ffffff',surface2:'#fbfbf6',accent:'#4d7c0f',accentBg:'#ecfccb',headerBg:'#3f4f1f',headerText:'#f7fee7',success:'#4d7c0f',successBg:'#ecfccb',warning:'#b45309',warningBg:'#fffbeb',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#a16207',infoBg:'#fef3c7'},
        'studio':{name:'Studio',desc:'Editorial plum',group:'expressive',ink:'#20162b',text:'#352247',text2:'#5b4a70',muted:'#7b6c8f',border:'#e4dbef',bg:'#faf7ff',surface:'#ffffff',surface2:'#fcfaff',accent:'#7c3aed',accentBg:'#ede9fe',headerBg:'#3b1d63',headerText:'#f5f3ff',success:'#15803d',successBg:'#f0fdf4',warning:'#b45309',warningBg:'#fffbeb',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#7c3aed',infoBg:'#ede9fe'},
        'marina':{name:'Marina',desc:'Ocean operations',group:'blue',ink:'#102028',text:'#163847',text2:'#4a6170',muted:'#6b7f8d',border:'#d7e4ea',bg:'#f3fafc',surface:'#ffffff',surface2:'#f7fcfd',accent:'#0891b2',accentBg:'#cffafe',headerBg:'#0f3a4a',headerText:'#ecfeff',success:'#15803d',successBg:'#f0fdf4',warning:'#b45309',warningBg:'#fffbeb',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#0891b2',infoBg:'#cffafe'},
        'meadow':{name:'Meadow',desc:'Sage planning',group:'green',ink:'#17211a',text:'#223229',text2:'#516158',muted:'#708078',border:'#d8e2da',bg:'#f5faf5',surface:'#ffffff',surface2:'#f8fcf8',accent:'#2f855a',accentBg:'#dcfce7',headerBg:'#1d3b2a',headerText:'#f0fdf4',success:'#2f855a',successBg:'#dcfce7',warning:'#a16207',warningBg:'#fefce8',danger:'#b91c1c',dangerBg:'#fef2f2',info:'#0f766e',infoBg:'#ccfbf1'}
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

    const rotate = (hex, degrees, saturation = .68, lightness = .50) => {
        const [h, s] = rgbToHsl(hexToRgb(hex));
        return rgbToHex(hslToRgb([(h + degrees + 360) % 360, Math.max(s, saturation), lightness]));
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

    const set = (name, value) => document.documentElement.style.setProperty(name, value);

    const installTokens = palette => {
        const chart2 = rotate(palette.accent, 42);
        const chart4 = rotate(palette.accent, 255, .62, .52);
        const chart8 = rotate(palette.accent, 315, .58, .50);
        const early = rotate(palette.warning, 18, .72, .48);
        const missing = rotate(palette.danger, -18, .70, .48);

        const tokens = {
            "--canvas": palette.bg,
            "--surface": palette.surface2,
            "--surface-raised": palette.surface,
            "--surface-muted": palette.surface2,
            "--surface-soft": palette.surface2,
            "--card-surface": palette.surface2,
            "--ink": palette.ink,
            "--ink-soft": palette.text2,
            "--muted": palette.muted,
            "--line": palette.border,
            "--line-soft": mix(palette.border, palette.surface, .58),
            "--line-strong": mix(palette.border, palette.text2, .30),
            "--primary": palette.accent,
            "--primary-hover": darken(palette.accent, .08),
            "--primary-strong": palette.headerBg,
            "--primary-soft": palette.accentBg,
            "--secondary": palette.info,
            "--secondary-hover": darken(palette.info, .08),
            "--secondary-dark": darken(palette.info, .14),
            "--secondary-soft": palette.infoBg,
            "--good": palette.success,
            "--good-soft": palette.successBg,
            "--warn": palette.warning,
            "--warn-soft": palette.warningBg,
            "--serious": early,
            "--serious-soft": mix(palette.surface, early, .10),
            "--critical": palette.danger,
            "--critical-soft": palette.dangerBg,
            "--table-header": palette.headerBg,
            "--table-header-text": palette.headerText,
            "--table-header-line": mix(palette.headerBg, palette.headerText, .22),
            "--table-row": palette.surface,
            "--table-row-alt": palette.surface2,
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
            "--chart-1": palette.accent,
            "--chart-2": chart2,
            "--chart-3": palette.success,
            "--chart-4": chart4,
            "--chart-5": palette.warning,
            "--chart-6": palette.info,
            "--chart-7": palette.danger,
            "--chart-8": chart8,
            "--chart-operational": palette.accent,
            "--chart-timesheet": chart2,
            "--chart-approval": chart4,
            "--chart-attendance": palette.success,
            "--chart-billable": palette.accent,
            "--chart-nonbillable": chart2,
            "--chart-training": chart4,
            "--chart-office": palette.success,
            "--chart-grid": mix(palette.border, palette.surface, .58),
            "--chart-axis": mix(palette.border, palette.text2, .30),
            "--chart-muted": palette.muted,
            "--chart-ink": palette.ink,
            "--chart-tooltip": palette.headerBg,
            "--chart-tooltip-text": palette.headerText,
            "--chart-heat-low": palette.accentBg,
            "--chart-heat-high": darken(palette.accent, .14),
            "--signal-punch": palette.info,
            "--signal-punch-soft": palette.infoBg,
            "--signal-timesheet": chart4,
            "--signal-timesheet-soft": mix(palette.surface, chart4, .11),
            "--signal-late": palette.warning,
            "--signal-late-soft": palette.warningBg,
            "--signal-early": early,
            "--signal-early-soft": mix(palette.surface, early, .11),
            "--signal-short": palette.danger,
            "--signal-short-soft": palette.dangerBg,
            "--signal-missing": missing,
            "--signal-missing-soft": mix(palette.surface, missing, .11)
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
        ink: cssValue("--chart-ink", "#0F172A"),
        tooltip: cssValue("--chart-tooltip", "#0F172A"),
        tooltipText: cssValue("--chart-tooltip-text", "#F8FAFC"),
        heatLow: cssValue("--chart-heat-low", "#EFF6FF"),
        heatHigh: cssValue("--chart-heat-high", "#1D4ED8"),
        surface: cssValue("--surface-raised", "#FFFFFF")
    });

    const apply = value => {
        const theme = normalize(value);
        const palette = palettes[theme];
        document.documentElement.dataset.theme = theme;
        installTokens(palette);

        try { localStorage.setItem(storageKey, theme); } catch { }

        window.dispatchEvent(new CustomEvent("epa-theme-changed", {
            detail: { theme, palette: chartPalette() }
        }));

        window.epaCharts?.refreshTheme?.();
        window.epaAnalyticsCharts?.refreshTheme?.();
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
