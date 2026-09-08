// ECharts wrapper. Every colour is read live from the --color-* custom properties Tailwind's
// @theme block in wwwroot/tailwind-input.css generates — no separate palette to keep in sync.
(function () {
    const instances = new Map();
    const observers = new Map();
    const themeUpdaters = new Map();
    let drilldownRef = null;

    const cssVar = (name, fallback) => getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;
    const palette = () => ({
        series:Array.from({length:8},(_,i)=>cssVar(`--color-chart-${i+1}`,'#33619f')),
        operational:cssVar('--color-petrol','#0f5f7a'), timesheet:cssVar('--color-chart-2','#33619f'), approval:cssVar('--color-chart-3','#6f5bb0'), attendance:cssVar('--color-chart-5','#2f8189'),
        billable:cssVar('--color-chart-2','#33619f'), nonBillable:cssVar('--color-chart-6','#5f6b7a'), training:cssVar('--color-chart-3','#6f5bb0'), office:cssVar('--color-chart-7','#4a7fa8'), punch:cssVar('--color-chart-1','#0f5f7a'), underutilized:cssVar('--color-chart-4','#96588a'),
        good:cssVar('--color-good','#2f8f52'), warning:cssVar('--color-warning','#a37f00'), serious:cssVar('--color-serious','#cf7118'), critical:cssVar('--color-critical','#cf2a1e'), info:cssVar('--color-info','#2f5fa3'),
        grid:cssVar('--color-line','#ddd6c5'), axis:cssVar('--color-line','#ddd6c5'), muted:cssVar('--color-muted','#8a8578'), inkSoft:cssVar('--color-ink-soft','#4e4c45'), ink:cssVar('--color-ink','#1a1a18'),
        tooltip:cssVar('--color-chassis','#232a33'), tooltipText:cssVar('--color-on-chassis','#f2eee4'),
        // Charts sit in a recessed `well`, so the plot ground is the well colour.
        surface:cssVar('--color-well','#e9e4d6'), missing:cssVar('--color-missing','#aca696'), missingSurface:cssVar('--color-well-deep','#ded8c7'), naSurface:cssVar('--color-line-soft','#e8e2d4'),
        heatScale:Array.from({length:5},(_,i)=>cssVar(`--color-heat-${i+1}`,'#35619f')), reducedMotion:window.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches || false
    });

    const roleColor = (role, index, p) => {
        switch (String(role || '').toLowerCase()) {
            case 'operational': return p.operational;
            case 'timesheet': return p.timesheet;
            case 'approval': return p.approval;
            case 'attendance': return p.attendance;
            case 'billable': return p.billable;
            case 'nonbillable': case 'non-billable': return p.nonBillable;
            case 'training': return p.training;
            case 'office': return p.office;
            case 'punch': return p.punch;
            case 'underutilized': return p.underutilized;
            case 'good': case 'success': return p.good;
            case 'warning': case 'caution': return p.warning;
            case 'serious': return p.serious;
            case 'critical': return p.critical;
            case 'info': case 'informational': return p.info;
            default: {
                const match = String(role || '').match(/^categorical-(\d+)$/i);
                if (match) return p.series[(Number(match[1]) - 1) % p.series.length];
                return p.series[index % p.series.length];
            }
        }
    };

    const animation = () => palette().reducedMotion
        ? { animation:false, animationDuration:0, animationDurationUpdate:0 }
        : { animation:true, animationDuration:520, animationEasing:'cubicOut', animationDurationUpdate:260 };

    const tooltip = p => ({
        backgroundColor:p.tooltip, borderWidth:0, padding:[9,12],
        textStyle:{color:p.tooltipText,fontSize:12.5,lineHeight:18},
        extraCssText:'box-shadow:0 8px 20px rgba(10,16,28,.24);border-radius:6px;'
    });

    function registerDrilldown(ref) { drilldownRef = ref; }
    function drill(name) {
        if (!drilldownRef || !name) return;
        drilldownRef.invokeMethodAsync('DrilldownTo', name).catch(() => {});
    }

    function ensure(id) {
        let chart = instances.get(id);
        if (chart && !chart.isDisposed()) return chart;
        const el = document.getElementById(id);
        if (!el) return null;
        chart = echarts.init(el, null, { renderer:'svg' });
        instances.set(id, chart);
        const ro = new ResizeObserver(() => chart.resize());
        ro.observe(el);
        observers.set(id, ro);
        return chart;
    }

    // A flex/grid-derived container (anything sized via flex-1/h-full rather than a fixed
    // chart-bay-* height class) isn't guaranteed to have its final size the instant a chart
    // draws into it - especially on a redraw triggered by a focus change, where several
    // siblings are reflowing in the same pass. One requestAnimationFrame wasn't always enough;
    // two back-to-back covers a layout that takes more than a single frame to settle. Cheap
    // no-op if the chart was already sized correctly.
    function deferredResize(chart) {
        requestAnimationFrame(() => {
            if (chart.isDisposed()) return;
            chart.resize();
            requestAnimationFrame(() => { if (!chart.isDisposed()) chart.resize(); });
        });
    }

    function registerThemeUpdater(id, updater) { if (typeof updater === 'function') themeUpdaters.set(id, updater); }

    function dispose(id) {
        const chart = instances.get(id);
        if (chart && !chart.isDisposed()) chart.dispose();
        instances.delete(id);
        const ro = observers.get(id);
        if (ro) ro.disconnect();
        observers.delete(id);
        themeUpdaters.delete(id);
    }

    function refreshTheme() {
        const p = palette();
        for (const [id, update] of themeUpdaters) {
            const chart = instances.get(id);
            if (!chart || chart.isDisposed()) { themeUpdaters.delete(id); continue; }
            try { update(p); } catch { }
        }
    }

    function gauge(id, value, band) {
        const chart = ensure(id); if (!chart) return;
        const renderTheme = p => {
            const color = roleColor(band || 'operational', 0, p);
            chart.setOption({ animationDurationUpdate:0, series:[{
                progress:{itemStyle:{color}}, axisLine:{lineStyle:{color:[[1,p.naSurface]]}},
                detail:{color:p.ink}
            }]}, {lazyUpdate:true,silent:true});
        };
        const p = palette(), color = roleColor(band || 'operational', 0, p);
        chart.setOption({
            ...animation(),
            series:[{
                type:'gauge', startAngle:210, endAngle:-30, min:0, max:100, radius:'92%',
                progress:{show:true,width:14,itemStyle:{color}}, axisLine:{lineStyle:{width:14,color:[[1,p.naSurface]]}},
                axisTick:{show:false}, splitLine:{show:false}, axisLabel:{show:false}, pointer:{show:false}, anchor:{show:false},
                detail:{valueAnimation:!p.reducedMotion,fontSize:30,fontWeight:650,color:p.ink,offsetCenter:[0,'10%'],formatter:v=>v.toFixed(1)},
                title:{show:false}, data:[{value}]
            }]
        }, true);
        registerThemeUpdater(id, renderTheme);
    }

    function radar(id, indicatorNames, maxValue, series) {
        const chart = ensure(id); if (!chart) return;
        const styledData = p => series.map((s,i) => {
            const color = roleColor(s.role, i, p);
            return {name:s.name,value:s.values,symbol:s.dashed?'none':'circle',
                lineStyle:{color,width:2,type:s.dashed?'dashed':'solid'},areaStyle:{color,opacity:s.dashed?0:.11},itemStyle:{color}};
        });
        const applyTheme = p => chart.setOption({animationDurationUpdate:0,tooltip:tooltip(p),radar:[{axisName:{color:p.ink,fontSize:11.5},splitLine:{lineStyle:{color:p.grid}},axisLine:{lineStyle:{color:p.grid}}}],series:[{data:styledData(p)}]}, {lazyUpdate:true,silent:true});
        const p = palette();
        chart.setOption({
            ...animation(), tooltip:{...tooltip(p),trigger:'item',formatter:x=>`<strong>${x.name}</strong><br/>${indicatorNames.map((n,i)=>`${n}: <b>${x.value[i].toFixed(0)}</b>`).join('<br/>')}`},
            legend:{show:false}, radar:{indicator:indicatorNames.map(n=>({name:n,max:maxValue})),radius:'68%',axisName:{color:p.ink,fontSize:11.5},splitArea:{areaStyle:{color:['transparent']}},splitLine:{lineStyle:{color:p.grid}},axisLine:{lineStyle:{color:p.grid}}},
            series:[{type:'radar',data:styledData(p)}]
        }, true);
        registerThemeUpdater(id, applyTheme);
        deferredResize(chart);
    }

    function scatter(id, points, ceilingX, targetY) {
        const chart = ensure(id); if (!chart) return;
        const byBand = {optimal:[],high:[],under:[]};
        for (const point of points) (byBand[point.band] || byBand.under).push([point.x,point.y,point.name,point.tooltip]);
        const config = [
            {key:'optimal',name:'Optimal',role:'good',symbol:'circle'},
            {key:'high',name:'High workload',role:'warning',symbol:'diamond'},
            {key:'under',name:'Underutilized',role:'underutilized',symbol:'rect'}
        ];
        const seriesTheme = p => config.map((item,i)=>({itemStyle:{color:roleColor(item.role,i,p),borderColor:p.surface,borderWidth:2}}));
        const applyTheme = p => chart.setOption({animationDurationUpdate:0,tooltip:tooltip(p),legend:{textStyle:{color:p.inkSoft,fontSize:11.5}},xAxis:{nameTextStyle:{color:p.inkSoft,fontSize:11.5},axisLine:{lineStyle:{color:p.axis}},axisLabel:{fontSize:11,color:p.inkSoft},splitLine:{lineStyle:{color:p.grid}}},yAxis:{axisLabel:{fontSize:11,color:p.inkSoft},splitLine:{lineStyle:{color:p.grid}}},series:[...seriesTheme(p),{markLine:{label:{color:p.inkSoft,fontSize:11},lineStyle:{color:p.axis,type:'dashed'}}}]}, {lazyUpdate:true,silent:true});
        const p = palette();
        chart.setOption({
            ...animation(), grid:{left:48,right:16,top:20,bottom:70}, tooltip:{...tooltip(p),trigger:'item',formatter:x=>`<strong>${x.data[3]}</strong><br/><span style="opacity:.72">Click the point to open the profile</span>`},
            legend:{bottom:4,itemWidth:11,itemHeight:11,textStyle:{fontSize:11.5,color:p.inkSoft},data:config.map(x=>x.name)},
            xAxis:{name:'Average punch hours per accountable day',nameLocation:'middle',nameGap:25,nameTextStyle:{color:p.inkSoft,fontSize:11.5},min:0,max:12,splitLine:{lineStyle:{color:p.grid}},axisLine:{lineStyle:{color:p.axis}},axisLabel:{fontSize:11,color:p.inkSoft}},
            yAxis:{min:0,max:130,axisLabel:{formatter:'{value}%',fontSize:11,color:p.inkSoft},splitLine:{lineStyle:{color:p.grid}},axisLine:{show:false}},
            series:[
                ...config.map((item,i)=>({name:item.name,type:'scatter',symbol:item.symbol,symbolSize:item.symbol==='diamond'?19:17,cursor:'pointer',itemStyle:{color:roleColor(item.role,i,p),borderColor:p.surface,borderWidth:2},emphasis:{scale:1.22,itemStyle:{borderWidth:3,shadowBlur:7,shadowColor:'rgba(0,0,0,.25)'}},data:byBand[item.key]})),
                {type:'line',silent:true,symbol:'none',data:[],markLine:{symbol:'none',label:{fontSize:11,color:p.inkSoft},lineStyle:{type:'dashed',color:p.axis},data:[{xAxis:ceilingX,label:{formatter:ceilingX+' h/day'}},{yAxis:targetY,label:{formatter:targetY+'% target'}}]}}
            ]
        }, true);
        chart.off('click'); chart.on('click',x=>{if(x.componentType==='series'&&x.seriesType==='scatter'&&x.data)drill(x.data[2]);});
        registerThemeUpdater(id, applyTheme);
    }

    function trend(id, categories, values, forecastValue) {
        const chart = ensure(id); if (!chart) return;
        const data = values.slice(), hasForecast = forecastValue != null;
        const applyTheme = p => chart.setOption({animationDurationUpdate:0,tooltip:tooltip(p),xAxis:{axisLine:{lineStyle:{color:p.axis}},axisLabel:{fontSize:11,color:p.inkSoft}},yAxis:{splitLine:{lineStyle:{color:p.grid}},axisLabel:{fontSize:11,color:p.inkSoft}},series:[{lineStyle:{color:p.operational,width:2},itemStyle:{color:p.operational}},...(hasForecast?[{lineStyle:{color:p.operational,type:'dashed',width:2},itemStyle:{color:p.surface,borderColor:p.operational,borderWidth:2}}]:[])]}, {lazyUpdate:true,silent:true});
        const p = palette();
        const forecastSeries = !hasForecast?[]:[{name:'Forecast',type:'line',symbol:'diamond',symbolSize:9,lineStyle:{color:p.operational,type:'dashed',width:2},itemStyle:{color:p.surface,borderColor:p.operational,borderWidth:2},data:[...Array(categories.length-1).fill(null),data[data.length-1],forecastValue],label:{show:true,formatter:x=>x.dataIndex===categories.length?x.value.toFixed(1):'',position:'top',fontWeight:650,color:p.ink,fontSize:11.5}}];
        chart.setOption({...animation(),grid:{left:44,right:20,top:26,bottom:34},tooltip:{...tooltip(p),trigger:'axis',formatter:params=>`<strong>${params[0].axisValueLabel}</strong><br/>${params.filter(x=>x.value!=null).map(x=>`${x.seriesName}: <b>${(+x.value).toFixed(1)}</b>${x.seriesName==='Forecast'?' <span style="opacity:.72">(projected)</span>':''}`).join('<br/>')}`},xAxis:{type:'category',data:[...categories,...(hasForecast?['Forecast']:[])],axisLine:{lineStyle:{color:p.axis}},axisLabel:{fontSize:11,color:p.inkSoft}},yAxis:{min:0,max:100,splitLine:{lineStyle:{color:p.grid}},axisLine:{show:false},axisLabel:{fontSize:11,color:p.inkSoft}},series:[{name:'Team score',type:'line',smooth:false,symbol:'circle',symbolSize:8,lineStyle:{color:p.operational,width:2},itemStyle:{color:p.operational},data},...forecastSeries]}, true);
        registerThemeUpdater(id, applyTheme);
    }

    function multiline(id, categories, series) {
        const chart = ensure(id); if (!chart) return;
        const applyTheme = p => chart.setOption({animationDurationUpdate:0,tooltip:tooltip(p),legend:{textStyle:{fontSize:11.5,color:p.inkSoft}},xAxis:{axisLine:{lineStyle:{color:p.axis}},axisLabel:{fontSize:11,color:p.inkSoft}},yAxis:{splitLine:{lineStyle:{color:p.grid}},axisLabel:{fontSize:11,color:p.inkSoft}},series:series.map((s,i)=>{const color=roleColor(s.role,i,p);return{lineStyle:{color,width:2,type:s.lineStyle||'solid'},itemStyle:{color},symbol:s.symbol||(['circle','diamond','rect','triangle'][i%4])};})}, {lazyUpdate:true,silent:true});
        const p = palette();
        chart.setOption({...animation(),grid:{left:44,right:20,top:38,bottom:34},legend:{top:0,itemWidth:11,itemHeight:11,textStyle:{fontSize:11.5,color:p.inkSoft}},tooltip:{...tooltip(p),trigger:'axis',formatter:params=>`<strong>${params[0].axisValueLabel}</strong><br/>${params.filter(x=>x.value!=null).map(x=>`${x.marker}${x.seriesName}: <b>${(+x.value).toFixed(1)}</b>`).join('<br/>')}`},xAxis:{type:'category',data:categories,axisLine:{lineStyle:{color:p.axis}},axisLabel:{fontSize:11,color:p.inkSoft}},yAxis:{min:0,max:100,splitLine:{lineStyle:{color:p.grid}},axisLine:{show:false},axisLabel:{fontSize:11,color:p.inkSoft}},series:series.map((s,i)=>{const color=roleColor(s.role,i,p);return{name:s.name,type:'line',symbol:s.symbol||(['circle','diamond','rect','triangle'][i%4]),symbolSize:7,connectNulls:true,lineStyle:{color,width:2,type:s.lineStyle||'solid'},itemStyle:{color},data:s.values};})}, true);
        registerThemeUpdater(id, applyTheme);
    }

    // WCAG 2.x relative luminance / contrast, defined ONCE here and exported for atlas-charts.js,
    // which used to carry a second copy of the same algorithm. The previous version here parsed
    // hex by hard-coded offsets 1/3/5, so a 3-digit token like #abc — or any rgb()/oklch() value
    // getComputedStyle can legitimately return — produced NaN and silently forced white text.
    const rgbOf = hex => {
        const c = String(hex).replace('#','').trim();
        const f = c.length === 3 ? c.split('').map(x => x + x).join('') : c;
        const n = parseInt(f, 16);
        return Number.isNaN(n) ? [0,0,0] : [(n >> 16) & 255, (n >> 8) & 255, n & 255];
    };
    const srgbLinear = v => { v /= 255; return v <= .04045 ? v/12.92 : Math.pow((v+.055)/1.055, 2.4); };
    const relLuminance = ([r,g,b]) => .2126*srgbLinear(r) + .7152*srgbLinear(g) + .0722*srgbLinear(b);
    const contrastRatio = (a,b) => { const x=relLuminance(rgbOf(a)), y=relLuminance(rgbOf(b)); return (Math.max(x,y)+.05)/(Math.min(x,y)+.05); };
    const bestTextColor = fill => contrastRatio(fill,'#ffffff') >= contrastRatio(fill,'#101828') ? '#ffffff' : '#101828';
    const mixHex = (a,b,t) => { const [ar,ag,ab]=rgbOf(a), [br,bg,bb]=rgbOf(b), r=v=>Math.round(v);
        return `rgb(${r(ar+(br-ar)*t)},${r(ag+(bg-ag)*t)},${r(ab+(bb-ab)*t)})`; };
    const onFill = bestTextColor;
    const heatFill = (value,p) => p.heatScale[Math.max(0,Math.min(p.heatScale.length-1,Math.floor((Math.max(0,Math.min(100,Number(value)))/100)*p.heatScale.length)))] || p.heatScale[0];
    // The same four colours and the same 55/70/85 breakpoints as Analytics.ScoreBand /
    // BandTextClass / BandBgClass on the C# side - i.e. this is not a borrowed gradient, it's
    // literally the app's one severity scale for 0-100 scores, so a heatmap cell always means
    // the same thing a Score Distribution dot or a band label already means elsewhere.
    const severityColor = (value,p) => {
        const stops=[p.critical,p.serious,p.warning,p.good], breaks=[0,55,70,85,100];
        const v=Math.max(0,Math.min(100,value));
        let i=0; while(i<breaks.length-2 && v>=breaks[i+1]) i++;
        const span=breaks[i+1]-breaks[i]||1;
        return mixHex(stops[i],stops[i+1],(v-breaks[i])/span);
    };
    const buildHeatData = (xCategories,yCategories,cells,p) => {
        const source = new Map(cells.map(c=>[`${c.x}:${c.y}`,c]));
        const data=[];
        for(let y=0;y<yCategories.length;y++) for(let x=0;x<xCategories.length;x++) {
            const c=source.get(`${x}:${y}`);
            const state=c?.state==='na'?'na':c&&c.value!=null?'measured':'missing';
            const numeric=state==='measured'?Number(c.value):null;
            // c.severity opts a cell into the shared red-to-green severity gradient instead of the
            // default sequential blue magnitude scale (heatFill) - use it for "good vs bad" data
            // (scores, health checks); leave it unset for true density/volume heatmaps where
            // "more" isn't "worse".
            const fill=state==='measured'?(c.severity?severityColor(numeric,p):heatFill(numeric,p)):state==='na'?p.naSurface:p.missingSurface;
            // c.focused gives the redraw a reason to exist: clicking a point elsewhere on the page
            // focuses a person, which re-triggers every chart including this one - rather than
            // fight that (or have it silently redraw nothing differently), the focused row now
            // actually shows the focus, the same way every other chart on the page already does.
            data.push({
                value:[x,y,state==='measured'?numeric:-1], tooltip:c?.tooltip||(state==='na'?`${yCategories[y]} · ${xCategories[x]}: Not applicable`:`${yCategories[y]} · ${xCategories[x]}: No source data`), state,
                itemStyle:{color:fill,borderColor:c?.focused?p.ink:p.surface,borderWidth:c?.focused?2:1,borderRadius:2,opacity:state==='na'?.5:1,...(state==='missing'?{decal:{symbol:'rect',symbolSize:1,dashArrayX:[1,0],dashArrayY:[2,3],color:p.axis}}:{})},
                label:{show:true,color:state==='measured'?onFill(fill):p.muted,fontSize:11,fontWeight:c?.focused?750:600,formatter:state==='measured'?numeric.toFixed(1):'—'}
            });
        }
        return data;
    };

    function heatmap(id, xCategories, yCategories, cells, drillable) {
        const chart=ensure(id); if(!chart)return;
        // interval:0 forces every column label to render instead of ECharts silently dropping
        // ones it thinks would overlap (which is what was happening with 4 narrow columns) -
        // rotated so they still fit without actually colliding.
        const xLabel=p=>({interval:0,rotate:20,fontSize:11,color:p.inkSoft});
        // Prefixes each row with its rank (1 = worst) purely as a display label - the
        // underlying category value yCategories[i] is left untouched so the click handler's
        // drill(yCategories[...]) below still resolves to a real, matchable person name.
        const yLabel=p=>({fontSize:11.5,color:p.ink,formatter:(value,index)=>`${yCategories.length-index}. ${value}`});
        const applyTheme=p=>chart.setOption({animationDurationUpdate:0,tooltip:tooltip(p),xAxis:{axisLabel:xLabel(p),axisLine:{lineStyle:{color:p.axis}}},yAxis:{axisLabel:yLabel(p),axisLine:{show:false}},series:[{data:buildHeatData(xCategories,yCategories,cells,p)}]}, {lazyUpdate:true,silent:true});
        const p=palette();
        chart.setOption({...animation(),tooltip:{...tooltip(p),position:'top',formatter:x=>`<strong>${x.data.tooltip}</strong>${drillable&&x.data.state==='measured'?'<br/><span style="opacity:.72">Click to open profile</span>':''}`},grid:{left:162,right:12,top:10,bottom:44},xAxis:{type:'category',data:xCategories,splitArea:{show:false},axisLabel:xLabel(p),axisLine:{lineStyle:{color:p.axis}}},yAxis:{type:'category',data:yCategories,axisLabel:yLabel(p),axisLine:{show:false},splitArea:{show:false}},series:[{type:'heatmap',data:buildHeatData(xCategories,yCategories,cells,p),cursor:drillable?'pointer':'default',emphasis:{itemStyle:{shadowBlur:7,shadowColor:'rgba(0,0,0,.18)'}}}]},true);
        chart.off('click'); if(drillable)chart.on('click',x=>{if(x.componentType==='series'&&x.data?.state==='measured')drill(yCategories[x.data.value[1]]);});
        registerThemeUpdater(id,applyTheme);
        deferredResize(chart);
    }

    function network(id,nodes,links,hubName) {
        const chart=ensure(id); if(!chart)return;
        const maxDegree=Math.max(1,...nodes.map(n=>n.received+n.given));
        const nodeData=p=>nodes.map((n,i)=>{const isHub=n.name===hubName,degree=n.received+n.given,size=isHub?46:22+Math.round((degree/maxDegree)*14),fill=isHub?p.operational:p.naSurface;return{name:n.name,value:degree,tooltip:n.tooltip,symbolSize:size,itemStyle:{color:fill,borderColor:isHub?p.operational:p.axis,borderWidth:isHub?3:1.5},label:{show:true,formatter:n.initials,position:'inside',color:isHub?onFill(fill):p.ink,fontSize:isHub?13:11,fontWeight:650}};});
        const applyTheme=p=>chart.setOption({animationDurationUpdate:0,tooltip:tooltip(p),series:[{lineStyle:{color:p.axis},emphasis:{lineStyle:{color:p.operational}},data:nodeData(p)}]}, {lazyUpdate:true,silent:true});
        const p=palette();
        chart.setOption({...animation(),tooltip:{...tooltip(p),formatter:x=>x.dataType==='node'||x.dataType==='edge'?`<strong>${x.data.tooltip}</strong>${x.dataType==='node'?'<br/><span style="opacity:.72">Click to open profile</span>':''}`:''},series:[{type:'graph',layout:'circular',roam:true,draggable:true,cursor:'pointer',circular:{rotateLabel:false},lineStyle:{color:p.axis,curveness:.22,width:1.2,opacity:.8},emphasis:{focus:'adjacency',lineStyle:{width:2.4,color:p.operational},label:{fontSize:12}},data:nodeData(p),links,edgeSymbol:['none','arrow'],edgeSymbolSize:6}]},true);
        chart.off('click');chart.on('click',x=>{if(x.dataType==='node')drill(x.data.name);});registerThemeUpdater(id,applyTheme);
    }

    function bars(id,categories,values,colors,max,suffix,drillable) {
        const chart=ensure(id); if(!chart)return;
        const decimals=max!=null&&max<=5?2:1;
        const colorFor=(i,p)=>{const value=colors?.[i];return typeof value==='string'&&value.startsWith('role:')?roleColor(value.slice(5),i,p):p.series[i%p.series.length];};
        const applyTheme=p=>chart.setOption({animationDurationUpdate:0,tooltip:tooltip(p),xAxis:{splitLine:{lineStyle:{color:p.grid}},axisLabel:{fontSize:11,color:p.inkSoft}},yAxis:{axisLabel:{fontSize:11.5,color:p.ink}},series:[{data:values.map((v,i)=>({value:v,itemStyle:{color:colorFor(i,p),borderRadius:[0,4,4,0]}})),label:{color:p.ink,fontSize:11.5}}]}, {lazyUpdate:true,silent:true});
        const p=palette();
        chart.setOption({...animation(),grid:{left:140,right:50,top:10,bottom:10},tooltip:{...tooltip(p),trigger:'axis',axisPointer:{type:'shadow'},formatter:params=>{const x=params[0],hint=drillable?'<br/><span style="opacity:.72">Click to open profile</span>':'';return`<strong>${x.name}</strong><br/>${x.value.toFixed(decimals)}${suffix||''}${hint}`;}},xAxis:{min:0,max:max||null,splitLine:{lineStyle:{color:p.grid}},axisLabel:{fontSize:11,color:p.inkSoft},axisLine:{show:false}},yAxis:{type:'category',data:categories,axisLabel:{fontSize:11.5,color:p.ink,width:126,overflow:'truncate'},axisLine:{show:false},axisTick:{show:false}},series:[{type:'bar',cursor:drillable?'pointer':'default',data:values.map((v,i)=>({value:v,itemStyle:{color:colorFor(i,p),borderRadius:[0,4,4,0]}})),barWidth:14,label:{show:true,position:'right',formatter:x=>x.value.toFixed(decimals)+(suffix||''),fontSize:11.5,fontWeight:600,color:p.ink}}]},true);
        chart.off('click');if(drillable)chart.on('click',x=>{if(x.componentType==='series')drill(categories[x.dataIndex]);});registerThemeUpdater(id,applyTheme);
    }

    function verticalBars(id,categories,series,max,suffix) {
        const chart=ensure(id);if(!chart)return;
        const applyTheme=p=>chart.setOption({animationDurationUpdate:0,tooltip:tooltip(p),legend:{textStyle:{fontSize:11.5,color:p.inkSoft}},xAxis:{axisLine:{lineStyle:{color:p.axis}},axisLabel:{fontSize:11,color:p.inkSoft}},yAxis:{splitLine:{lineStyle:{color:p.grid}},axisLabel:{fontSize:11,color:p.inkSoft}},series:series.map((s,i)=>({itemStyle:{color:roleColor(s.role,i,p),borderRadius:[3,3,0,0]}}))}, {lazyUpdate:true,silent:true});
        const p=palette();
        chart.setOption({...animation(),grid:{left:44,right:20,top:38,bottom:34},legend:{show:series.length>1,top:0,itemWidth:11,itemHeight:11,textStyle:{fontSize:11.5,color:p.inkSoft}},tooltip:{...tooltip(p),trigger:'axis',axisPointer:{type:'shadow'},formatter:params=>`<strong>${params[0].axisValueLabel}</strong><br/>${params.filter(x=>x.value!=null).map(x=>`${x.marker}${x.seriesName}: <b>${(+x.value).toFixed(1)}${suffix||''}</b>`).join('<br/>')}`},xAxis:{type:'category',data:categories,axisLine:{lineStyle:{color:p.axis}},axisLabel:{fontSize:11,color:p.inkSoft}},yAxis:{min:0,max:max||null,splitLine:{lineStyle:{color:p.grid}},axisLine:{show:false},axisLabel:{fontSize:11,color:p.inkSoft,formatter:suffix?'{value}'+suffix:'{value}'}},series:series.map((s,i)=>({name:s.name,type:'bar',barGap:'20%',itemStyle:{color:roleColor(s.role,i,p),borderRadius:[3,3,0,0]},data:s.values}))},true);
        registerThemeUpdater(id,applyTheme);
    }

    window.epaCharts={bestTextColor,contrastRatio,mixHex,ensure,dispose,gauge,radar,scatter,trend,multiline,heatmap,network,bars,verticalBars,registerDrilldown,registerThemeUpdater,refreshTheme,roleColor,palette};
})();
