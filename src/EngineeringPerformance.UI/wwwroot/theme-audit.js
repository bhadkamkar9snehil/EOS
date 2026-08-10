(() => {
    const requiredTokens = [
        '--canvas','--surface','--card-surface','--surface-raised','--surface-inset','--surface-selected',
        '--ink','--ink-soft','--muted','--line','--line-strong','--focus-ring',
        '--primary','--on-primary','--secondary','--on-secondary',
        '--good','--on-good','--warn','--on-warning','--serious','--on-serious','--critical','--on-critical',
        '--table-header','--on-table-header','--chart-tooltip','--on-chart-tooltip',
        '--chart-1','--chart-2','--chart-3','--chart-4','--chart-5','--chart-6','--chart-7','--chart-8'
    ];

    const css = name => getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    const parseColor = value => {
        const probe = document.createElement('span');
        probe.style.color = value;
        probe.style.position = 'fixed';
        probe.style.visibility = 'hidden';
        document.body.appendChild(probe);
        const computed = getComputedStyle(probe).color;
        probe.remove();
        const match = computed.match(/rgba?\((\d+(?:\.\d+)?)[,\s]+(\d+(?:\.\d+)?)[,\s]+(\d+(?:\.\d+)?)/);
        return match ? [Number(match[1]), Number(match[2]), Number(match[3])].map(x => x / 255) : null;
    };
    const linear = value => value <= .04045 ? value / 12.92 : Math.pow((value + .055) / 1.055, 2.4);
    const luminance = value => {
        const rgb = parseColor(value);
        if (!rgb) return null;
        const [r,g,b] = rgb.map(linear);
        return .2126*r + .7152*g + .0722*b;
    };
    const contrast = (a,b) => {
        const x = luminance(a), y = luminance(b);
        if (x == null || y == null) return null;
        return (Math.max(x,y)+.05)/(Math.min(x,y)+.05);
    };

    const pair = (foreground, background, minimum, label) => {
        const ratio = contrast(css(foreground), css(background));
        return { label, foreground, background, ratio: ratio == null ? null : Number(ratio.toFixed(2)), minimum, pass: ratio != null && ratio >= minimum };
    };

    function current() {
        const missingTokens = requiredTokens.filter(name => !css(name));
        const contrastChecks = [
            pair('--ink','--card-surface',4.5,'Body text on cards'),
            pair('--ink-soft','--card-surface',4.5,'Secondary analytical text on cards'),
            pair('--on-primary','--primary',4.5,'Primary control foreground'),
            pair('--on-secondary','--secondary',4.5,'Secondary control foreground'),
            pair('--on-good','--good',4.5,'Success foreground'),
            pair('--on-warning','--warn',4.5,'Warning foreground'),
            pair('--on-serious','--serious',4.5,'Serious foreground'),
            pair('--on-critical','--critical',4.5,'Critical foreground'),
            pair('--on-table-header','--table-header',4.5,'Table header foreground'),
            pair('--on-chart-tooltip','--chart-tooltip',4.5,'Chart tooltip foreground'),
            pair('--focus-ring','--canvas',3,'Focus ring on canvas'),
            pair('--focus-ring','--card-surface',3,'Focus ring on card'),
            pair('--focus-ring','--surface-raised',3,'Focus ring on raised surface')
        ];

        const chartColors = Array.from({length:8},(_,i)=>css(`--chart-${i+1}`));
        const duplicateChartColors = chartColors.filter((color,index) => chartColors.indexOf(color) !== index);
        const diagnostics = window.epaTheme?.diagnostics?.(window.epaTheme?.get?.()) || null;

        return {
            theme: window.epaTheme?.get?.() || document.documentElement.dataset.theme,
            mode: window.epaTheme?.mode?.get?.(),
            intensity: window.epaTheme?.intensity?.get?.(),
            missingTokens,
            contrastChecks,
            chartColors,
            duplicateChartColors:[...new Set(duplicateChartColors)],
            diagnostics,
            pass: missingTokens.length === 0 && duplicateChartColors.length === 0 && contrastChecks.every(x => x.pass)
        };
    }

    function typography(root = document.body) {
        const issues = [];
        const ignoredTags = new Set(['SCRIPT','STYLE','SVG','PATH','OPTION']);
        root.querySelectorAll('*').forEach(element => {
            if (ignoredTags.has(element.tagName) || element.children.length > 0 || !element.textContent?.trim()) return;
            const style = getComputedStyle(element);
            if (style.display === 'none' || style.visibility === 'hidden') return;
            const size = parseFloat(style.fontSize);
            if (Number.isFinite(size) && size < 10.5) {
                issues.push({ element: element.tagName.toLowerCase(), className: element.className || '', text: element.textContent.trim().slice(0,80), fontSize:size });
            }
        });
        return { floor:10.5, issues, pass:issues.length===0 };
    }

    function allPalettes() {
        const themes = window.epaTheme?.palettes?.() || {};
        return Object.keys(themes).map(key => ({ key, ...window.epaTheme.diagnostics(key) }));
    }

    window.epaThemeAudit = {
        current,
        typography,
        allPalettes,
        run:() => {
            const result = { theme:current(), typography:typography() };
            result.pass = result.theme.pass && result.typography.pass;
            console.table(result.theme.contrastChecks);
            if (!result.typography.pass) console.table(result.typography.issues);
            return result;
        }
    };
})();
