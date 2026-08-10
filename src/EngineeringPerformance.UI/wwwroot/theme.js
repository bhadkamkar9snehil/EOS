(() => {
    const keys = {
        theme: 'epa-theme', mode: 'epa-theme-mode', intensity: 'epa-theme-intensity',
        density: 'epa-density', motion: 'epa-motion'
    };

    // Palette definitions contain identity only. Semantic meaning is resolved below and
    // remains stable across theme families. Dark palettes are independently tuned for
    // dense business data rather than copied verbatim from editor themes.
    const palettes = {
        graphite:{name:'Graphite',desc:'Executive neutral',category:'professional-light',dark:false,ink:'#0f172a',text2:'#334155',muted:'#64748b',border:'#dbe2ea',bg:'#f4f7fb',surface:'#ffffff',surface2:'#f8fafc',accent:'#2563eb',header:'#0f172a'},
        ledger:{name:'Ledger',desc:'Fintech calm',category:'professional-light',dark:false,ink:'#101828',text2:'#344054',muted:'#667085',border:'#d0d5dd',bg:'#f7f8fa',surface:'#ffffff',surface2:'#f9fafb',accent:'#175cd3',header:'#101828'},
        sterling:{name:'Sterling',desc:'Steel neutral',category:'professional-light',dark:false,ink:'#111827',text2:'#4b5563',muted:'#6b7280',border:'#d1d5db',bg:'#f3f4f6',surface:'#ffffff',surface2:'#f8fafc',accent:'#0f766e',header:'#1f2937'},
        canvas:{name:'Canvas',desc:'Soft neutral',category:'professional-light',dark:false,ink:'#111827',text2:'#4b5563',muted:'#6b7280',border:'#e5e7eb',bg:'#f9fafb',surface:'#ffffff',surface2:'#f8fafc',accent:'#475569',header:'#1f2937'},
        quartz:{name:'Quartz',desc:'Monochrome crisp',category:'professional-light',dark:false,ink:'#0a0a0a',text2:'#404040',muted:'#737373',border:'#d4d4d4',bg:'#f5f5f5',surface:'#ffffff',surface2:'#fafafa',accent:'#262626',header:'#171717'},
        sandstone:{name:'Sandstone',desc:'Warm engineering neutral',category:'warm-light',dark:false,ink:'#1f2937',text2:'#4b5563',muted:'#6b7280',border:'#e4ded4',bg:'#faf7f2',surface:'#fffdf9',surface2:'#f7f2ea',accent:'#92400e',header:'#292524'},
        'contrast-light':{name:'High Contrast Light',desc:'Maximum daylight legibility',category:'high-contrast',dark:false,ink:'#050505',text2:'#202020',muted:'#4b4b4b',border:'#8a8a8a',bg:'#eeeeee',surface:'#ffffff',surface2:'#f7f7f7',accent:'#0037a8',header:'#050505'},

        dracula:{name:'Amethyst Night',desc:'Purple-led low-light palette',category:'professional-dark',dark:true,ink:'#f8f8f2',text2:'#d0d2e2',muted:'#a6a8c0',border:'#5c6078',bg:'#242630',surface:'#414456',surface2:'#303341',accent:'#bd93f9',header:'#1b1d26'},
        gruvbox:{name:'Copper Night',desc:'Warm low-light contrast',category:'editor-inspired',dark:true,ink:'#f4e8c8',text2:'#d1c4a8',muted:'#aea18d',border:'#665c54',bg:'#242424',surface:'#3d3835',surface2:'#302e2d',accent:'#fe8019',header:'#1b1d1e'},
        monokai:{name:'Magenta Night',desc:'Expressive dark palette',category:'editor-inspired',dark:true,ink:'#fafaf5',text2:'#d3d4ca',muted:'#a7a89f',border:'#5e5f55',bg:'#24251f',surface:'#3d3e34',surface2:'#303129',accent:'#f92672',header:'#1b1c18'},
        everforest:{name:'Forest Night',desc:'Soft green low-light palette',category:'professional-dark',dark:true,ink:'#e8dcc5',text2:'#c3ccc1',muted:'#adb6ab',border:'#637077',bg:'#283137',surface:'#414e54',surface2:'#344047',accent:'#a7c080',header:'#20272b'},
        'solarized-dark':{name:'Solar Night',desc:'Amber-led analytical dark',category:'professional-dark',dark:true,ink:'#f5efdd',text2:'#bcc9c9',muted:'#92a4aa',border:'#23616d',bg:'#00252e',surface:'#0b4652',surface2:'#063842',accent:'#d1a51a',header:'#001c24'},
        'contrast-dark':{name:'High Contrast Dark',desc:'Maximum low-light legibility',category:'high-contrast',dark:true,ink:'#ffffff',text2:'#ededed',muted:'#c4c4c4',border:'#888888',bg:'#000000',surface:'#1c1c1c',surface2:'#0d0d0d',accent:'#8ab4ff',header:'#000000'}
    };

    const semantic = {
        light:{good:'#16794A',warning:'#946200',serious:'#B45309',critical:'#B42318',info:'#0F6E9E',missing:'#667085'},
        dark:{good:'#67D391',warning:'#F0C967',serious:'#F3A56B',critical:'#FF8178',info:'#82BCE4',missing:'#B2BAC5'}
    };

    const intensityScale = {restrained:.76,balanced:1,vivid:1.18};
    const safeGet=(key,fallback)=>{try{return localStorage.getItem(key)||fallback;}catch{return fallback;}};
    const safeSet=(key,value)=>{try{localStorage.setItem(key,value);}catch{}};
    const normalizeTheme=value=>Object.prototype.hasOwnProperty.call(palettes,value)?value:'graphite';
    const normalizeChoice=(value,choices,fallback)=>choices.includes(value)?value:fallback;
    const clamp01=value=>Math.max(0,Math.min(1,value));

    const hexToSrgb=value=>{
        const hex=String(value).replace('#','');
        return [0,2,4].map(i=>parseInt(hex.slice(i,i+2),16)/255);
    };
    const linear=value=>value<=.04045?value/12.92:Math.pow((value+.055)/1.055,2.4);
    const encoded=value=>value<=.0031308?12.92*value:1.055*Math.pow(value,1/2.4)-.055;
    const srgbToOklab=rgb=>{
        const [r,g,b]=rgb.map(linear);
        const l=Math.cbrt(.4122214708*r+.5363325363*g+.0514459929*b);
        const m=Math.cbrt(.2119034982*r+.6806995451*g+.1073969566*b);
        const s=Math.cbrt(.0883024619*r+.2817188376*g+.6299787005*b);
        return [.2104542553*l+.793617785*m-.0040720468*s,1.9779984951*l-2.428592205*m+.4505937099*s,.0259040371*l+.7827717662*m-.808675766*s];
    };
    const oklabToLinear=([L,a,b])=>{
        const l=Math.pow(L+.3963377774*a+.2158037573*b,3);
        const m=Math.pow(L-.1055613458*a-.0638541728*b,3);
        const s=Math.pow(L-.0894841775*a-1.291485548*b,3);
        return [4.0767416621*l-3.3077115913*m+.2309699292*s,-1.2684380046*l+2.6097574011*m-.3413193965*s,-.0041960863*l-.7034186147*m+1.707614701*s];
    };
    const toOklch=hex=>{
        const [L,a,b]=srgbToOklab(hexToSrgb(hex));
        return [L,Math.sqrt(a*a+b*b),(Math.atan2(b,a)*180/Math.PI+360)%360];
    };
    const toHex=([L,C,H])=>{
        const h=H*Math.PI/180;
        let chroma=Math.max(0,C),rgb;
        for(let i=0;i<24;i++){
            rgb=oklabToLinear([clamp01(L),chroma*Math.cos(h),chroma*Math.sin(h)]);
            if(rgb.every(v=>v>=0&&v<=1))break;
            chroma*=.9;
        }
        rgb=(rgb||[0,0,0]).map(v=>clamp01(encoded(clamp01(v))));
        return '#'+rgb.map(v=>Math.round(v*255).toString(16).padStart(2,'0')).join('').toUpperCase();
    };
    const mix=(a,b,amount=.5)=>{
        const A=toOklch(a),B=toOklch(b);let delta=B[2]-A[2];
        if(delta>180)delta-=360;if(delta<-180)delta+=360;
        return toHex([A[0]+(B[0]-A[0])*amount,A[1]+(B[1]-A[1])*amount,(A[2]+delta*amount+360)%360]);
    };
    const shiftLightness=(hex,amount)=>{const c=toOklch(hex);return toHex([clamp01(c[0]+amount),c[1],c[2]]);};
    const luminance=hex=>{const [r,g,b]=hexToSrgb(hex).map(linear);return .2126*r+.7152*g+.0722*b;};
    const contrast=(a,b)=>{const x=luminance(a),y=luminance(b);return (Math.max(x,y)+.05)/(Math.min(x,y)+.05);};
    const foregroundFor=fill=>contrast(fill,'#FFFFFF')>=contrast(fill,'#101828')?'#FFFFFF':'#101828';

    const categorical=(palette,strengthName)=>{
        const base=toOklch(palette.accent),factor=intensityScale[strengthName]||1;
        const offsets=[0,52,118,188,252,308,82,222];
        const lightness=palette.dark?[.72,.78,.73,.80,.74,.79,.69,.76]:[.47,.51,.45,.52,.46,.50,.43,.49];
        const baseC=Math.max(.075,Math.min(.145,base[1]||.1))*factor;
        return offsets.map((offset,i)=>toHex([lightness[i],Math.max(.06,Math.min(.18,baseC*(i%3===1?.94:1))),(base[2]+offset)%360]));
    };
    const heatScale=(accent,dark,strengthName)=>{
        const [,C,H]=toOklch(accent),factor=intensityScale[strengthName]||1;
        const levels=dark?[.25,.38,.51,.64,.78]:[.94,.82,.68,.53,.39];
        return levels.map((L,i)=>toHex([L,Math.max(.035,Math.min(.14,C*factor*(.75+i*.07))),H]));
    };
    const focusColor=(accent,surfaces,dark)=>{
        const candidates=[accent,dark?'#A8CAFF':'#174EA6',dark?'#FFD166':'#7A2E00'];
        return candidates.find(c=>Math.min(...surfaces.map(s=>contrast(c,s)))>=3)||candidates[1];
    };

    const cssValue=(name,fallback)=>getComputedStyle(document.documentElement).getPropertyValue(name).trim()||fallback;
    const set=(name,value)=>document.documentElement.style.setProperty(name,value);
    const intensity=()=>normalizeChoice(safeGet(keys.intensity,'balanced'),['restrained','balanced','vivid'],'balanced');
    const density=()=>normalizeChoice(safeGet(keys.density,'comfortable'),['comfortable','compact'],'comfortable');
    const motion=()=>normalizeChoice(safeGet(keys.motion,'system'),['system','reduced','full'],'system');
    const mode=()=>normalizeChoice(safeGet(keys.mode,'manual'),['manual','system'],'manual');
    const systemMedia=window.matchMedia?.('(prefers-color-scheme: dark)');
    const reducedMedia=window.matchMedia?.('(prefers-reduced-motion: reduce)');
    const systemTheme=()=>systemMedia?.matches?'everforest':'graphite';
    const selectedTheme=()=>normalizeTheme(safeGet(keys.theme,'graphite'));
    const effectiveTheme=()=>mode()==='system'?systemTheme():selectedTheme();
    function isReducedMotion(){const setting=motion();return setting==='reduced'||(setting==='system'&&!!reducedMedia?.matches);}

    const resolve=palette=>{
        const isDark=palette.dark,strength=intensity(),sem=isDark?semantic.dark:semantic.light;
        const series=categorical(palette,strength),heat=heatScale(palette.accent,isDark,strength);
        const canvas=palette.bg;
        const base=palette.surface2;
        const inset=mix(canvas,base,isDark?.42:.58);
        const soft=mix(base,palette.surface,isDark?.26:.34);
        const card=mix(base,palette.surface,isDark?.60:.68);
        const raised=palette.surface;
        const selected=mix(card,palette.accent,isDark?.13:.075);
        const focus=focusColor(palette.accent,[canvas,base,card,raised,selected],isDark);
        const primaryHover=shiftLightness(palette.accent,isDark?.07:-.07);
        const selectionStrength=strength==='vivid'?30:strength==='restrained'?18:24;
        const derived=series[4];
        const tokens={
            'color-scheme':isDark?'dark':'light',
            '--canvas':canvas,'--surface-inset':inset,'--surface-muted':inset,'--surface':base,'--surface-soft':soft,'--card-surface':card,'--surface-raised':raised,'--surface-selected':selected,
            '--ink':palette.ink,'--ink-soft':palette.text2,'--muted':palette.muted,
            '--line':palette.border,'--line-soft':mix(palette.border,raised,isDark?.30:.58),'--line-strong':mix(palette.border,palette.text2,isDark?.22:.28),
            '--primary':palette.accent,'--primary-hover':primaryHover,'--primary-strong':isDark?shiftLightness(palette.accent,.12):palette.header,'--primary-soft':mix(card,palette.accent,isDark?.16:.10),'--on-primary':foregroundFor(palette.accent),'--primary-ink':foregroundFor(palette.accent),
            '--secondary':series[1],'--secondary-hover':shiftLightness(series[1],isDark?.06:-.06),'--secondary-dark':isDark?shiftLightness(series[1],.08):shiftLightness(series[1],-.10),'--secondary-soft':mix(card,series[1],isDark?.14:.09),'--on-secondary':foregroundFor(series[1]),'--secondary-ink':foregroundFor(series[1]),
            '--good':sem.good,'--good-soft':mix(card,sem.good,isDark?.13:.08),'--on-good':foregroundFor(sem.good),'--good-ink':foregroundFor(sem.good),
            '--warn':sem.warning,'--warn-soft':mix(card,sem.warning,isDark?.13:.08),'--on-warning':foregroundFor(sem.warning),'--warn-ink':foregroundFor(sem.warning),
            '--serious':sem.serious,'--serious-soft':mix(card,sem.serious,isDark?.13:.08),'--on-serious':foregroundFor(sem.serious),'--serious-ink':foregroundFor(sem.serious),
            '--critical':sem.critical,'--critical-soft':mix(card,sem.critical,isDark?.13:.08),'--on-critical':foregroundFor(sem.critical),'--critical-ink':foregroundFor(sem.critical),
            '--info':sem.info,'--info-soft':mix(card,sem.info,isDark?.13:.08),'--on-info':foregroundFor(sem.info),
            '--missing':sem.missing,'--missing-soft':mix(card,sem.missing,isDark?.13:.08),
            '--focus-ring':focus,'--focus-soft':`color-mix(in srgb, ${focus} 20%, transparent)`,
            '--table-header':palette.header,'--on-table-header':foregroundFor(palette.header),'--table-header-text':foregroundFor(palette.header),'--table-header-line':mix(palette.header,foregroundFor(palette.header),.22),'--table-row':card,'--table-row-alt':soft,'--table-row-hover':selected,
            '--nav-bg':palette.header,'--nav-line':mix(palette.header,foregroundFor(palette.header),.12),'--nav-text':mix(palette.header,foregroundFor(palette.header),.80),'--nav-text-strong':foregroundFor(palette.header),'--nav-muted':mix(palette.header,foregroundFor(palette.header),.64),'--nav-icon':mix(palette.header,foregroundFor(palette.header),.72),'--nav-hover':`color-mix(in srgb, ${palette.accent} ${Math.max(12,selectionStrength-6)}%, transparent)`,'--nav-selected':`color-mix(in srgb, ${palette.accent} ${selectionStrength}%, transparent)`,
            '--derived':derived,'--derived-soft':mix(card,derived,isDark?.12:.07),'--on-derived':foregroundFor(derived),
            '--shadow-resting':'none','--shadow-raised':isDark?'0 10px 28px rgba(0,0,0,.42),0 1px 0 rgba(255,255,255,.05) inset':'0 8px 22px rgba(20,28,40,.12)','--shadow-drawer':isDark?'0 28px 70px rgba(0,0,0,.64)':'0 28px 70px rgba(20,28,40,.28)','--shadow-nav':'none',
            '--chart-grid':mix(palette.border,card,isDark?.38:.64),'--chart-axis':mix(palette.border,palette.text2,.28),'--chart-muted':palette.muted,'--chart-ink':palette.ink,'--chart-tooltip':palette.header,'--on-chart-tooltip':foregroundFor(palette.header),'--chart-tooltip-text':foregroundFor(palette.header),'--chart-surface':raised,
            '--chart-operational':series[0],'--chart-timesheet':series[1],'--chart-attendance':series[2],'--chart-approval':series[3],'--chart-billable':series[0],'--chart-nonbillable':series[4],'--chart-training':series[3],'--chart-office':series[2],'--chart-punch':series[5],'--chart-underutilized':series[6],
            '--chart-missing':sem.missing,'--chart-missing-surface':mix(card,sem.missing,isDark?.12:.08),'--chart-na-surface':inset,
            '--signal-punch':series[5],'--signal-punch-soft':mix(card,series[5],.07),'--signal-timesheet':series[3],'--signal-timesheet-soft':mix(card,series[3],.07),'--signal-late':sem.warning,'--signal-late-soft':mix(card,sem.warning,.07),'--signal-early':sem.serious,'--signal-early-soft':mix(card,sem.serious,.07),'--signal-short':sem.critical,'--signal-short-soft':mix(card,sem.critical,.07),'--signal-missing':sem.critical,'--signal-missing-soft':mix(card,sem.critical,.07)
        };
        series.forEach((color,index)=>tokens[`--chart-${index+1}`]=color);
        heat.forEach((color,index)=>tokens[`--chart-heat-${index+1}`]=color);
        tokens['--chart-heat-low']=heat[0];tokens['--chart-heat-high']=heat[heat.length-1];
        return {tokens,canvas,base,inset,soft,card,raised,selected,focus,series,heat};
    };

    const installTokens=palette=>{
        const resolved=resolve(palette);
        Object.entries(resolved.tokens).forEach(([name,value])=>set(name,value));
        return resolved;
    };

    const chartPalette=()=>({
        series:Array.from({length:8},(_,i)=>cssValue(`--chart-${i+1}`,'#2563EB')),
        operational:cssValue('--chart-operational','#2563EB'),timesheet:cssValue('--chart-timesheet','#0F6E9E'),approval:cssValue('--chart-approval','#7C3AED'),attendance:cssValue('--chart-attendance','#16794A'),billable:cssValue('--chart-billable','#2563EB'),nonBillable:cssValue('--chart-nonbillable','#946200'),training:cssValue('--chart-training','#7C3AED'),office:cssValue('--chart-office','#16794A'),punch:cssValue('--chart-punch','#0F6E9E'),underutilized:cssValue('--chart-underutilized','#5B6FD8'),
        good:cssValue('--good','#16794A'),warning:cssValue('--warn','#946200'),serious:cssValue('--serious','#B45309'),critical:cssValue('--critical','#B42318'),info:cssValue('--info','#0F6E9E'),missing:cssValue('--chart-missing','#667085'),missingSurface:cssValue('--chart-missing-surface','#EAECF0'),naSurface:cssValue('--chart-na-surface','#F2F4F7'),
        grid:cssValue('--chart-grid','#E5E7EB'),axis:cssValue('--chart-axis','#CBD5E1'),muted:cssValue('--chart-muted','#64748B'),inkSoft:cssValue('--ink-soft','#475569'),ink:cssValue('--chart-ink','#0F172A'),tooltip:cssValue('--chart-tooltip','#0F172A'),tooltipText:cssValue('--on-chart-tooltip','#FFFFFF'),surface:cssValue('--chart-surface','#FFFFFF'),focus:cssValue('--focus-ring','#174EA6'),
        heatScale:Array.from({length:5},(_,i)=>cssValue(`--chart-heat-${i+1}`,'#2563EB')),reducedMotion:isReducedMotion()
    });

    const notify=theme=>window.dispatchEvent(new CustomEvent('epa-theme-changed',{detail:{theme,palette:chartPalette()}}));
    const install=(themeKey,announce=true)=>{
        const theme=normalizeTheme(themeKey);
        document.documentElement.dataset.theme=theme;
        document.documentElement.dataset.themeMode=mode();
        installTokens(palettes[theme]);
        if(announce)notify(theme);
        return theme;
    };
    const apply=value=>{const theme=normalizeTheme(value);safeSet(keys.theme,theme);safeSet(keys.mode,'manual');return install(theme);};
    const applyMode=value=>{const next=normalizeChoice(value,['manual','system'],'manual');safeSet(keys.mode,next);document.documentElement.dataset.themeMode=next;return install(next==='system'?systemTheme():selectedTheme());};
    const applyIntensity=value=>{const next=normalizeChoice(value,['restrained','balanced','vivid'],'balanced');safeSet(keys.intensity,next);install(effectiveTheme());return next;};
    const applyDensity=value=>{const next=normalizeChoice(value,['comfortable','compact'],'comfortable');safeSet(keys.density,next);document.documentElement.dataset.density=next;return next;};
    const applyMotion=value=>{const next=normalizeChoice(value,['system','reduced','full'],'system');safeSet(keys.motion,next);document.documentElement.dataset.motion=next;window.dispatchEvent(new CustomEvent('epa-motion-changed',{detail:{motion:next,reduced:isReducedMotion()}}));return next;};

    const diagnostics=value=>{
        const key=normalizeTheme(value),p=palettes[key],r=resolve(p),sem=p.dark?semantic.dark:semantic.light;
        const textChecks=[contrast(p.ink,r.card),contrast(p.text2,r.card),contrast(p.muted,r.card)];
        const focusChecks=[r.canvas,r.base,r.card,r.raised,r.selected].map(surface=>contrast(r.focus,surface));
        const surfaceDeltas={canvasBase:Math.abs(luminance(r.canvas)-luminance(r.base)),baseCard:Math.abs(luminance(r.base)-luminance(r.card)),cardRaised:Math.abs(luminance(r.card)-luminance(r.raised))};
        return {
            dark:p.dark,bodyContrast:textChecks[0],secondaryContrast:textChecks[1],mutedContrast:textChecks[2],
            focusContrast:Math.min(...focusChecks),categoricalDistinct:r.series.length===new Set(r.series).size,
            semanticContrast:{good:contrast(foregroundFor(sem.good),sem.good),warning:contrast(foregroundFor(sem.warning),sem.warning),serious:contrast(foregroundFor(sem.serious),sem.serious),critical:contrast(foregroundFor(sem.critical),sem.critical)},
            surfaceDeltas,pass:textChecks.every(x=>x>=4.5)&&Math.min(...focusChecks)>=3
        };
    };

    window.epaTheme={
        get:()=>normalizeTheme(document.documentElement.dataset.theme||effectiveTheme()),selected:selectedTheme,apply,chartPalette,palettes:()=>palettes,
        mode:{get:mode,apply:applyMode},intensity:{get:intensity,apply:applyIntensity},density:{get:density,apply:applyDensity},motion:{get:motion,apply:applyMotion,isReduced:isReducedMotion},diagnostics,
        initialize:()=>{document.documentElement.dataset.density=density();document.documentElement.dataset.motion=motion();document.documentElement.dataset.themeMode=mode();return install(effectiveTheme(),false);}
    };
    systemMedia?.addEventListener?.('change',()=>{if(mode()==='system')install(systemTheme());});
    reducedMedia?.addEventListener?.('change',()=>{if(motion()==='system')window.dispatchEvent(new CustomEvent('epa-motion-changed',{detail:{motion:'system',reduced:isReducedMotion()}}));});
    window.epaTheme.initialize();
})();
