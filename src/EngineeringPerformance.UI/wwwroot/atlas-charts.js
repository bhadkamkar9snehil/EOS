// EOS Performance Atlas renderers. Data comes from Razor; colour/contrast comes
// from the resolved theme contract. Theme refreshes patch presentation without
// recomputing application data or replaying the page render pipeline.
(() => {
    const instances=new Map(),renderers=new Map();
    let drilldownRef=null;
    const css=(name,fallback)=>getComputedStyle(document.documentElement).getPropertyValue(name).trim()||fallback;
    const palette=()=>window.epaTheme?.chartPalette?.()||{
        series:Array.from({length:8},(_,i)=>css(`--chart-${i+1}`,'#0f5f7a')),
        operational:css('--chart-operational','#0f5f7a'),attendance:css('--chart-attendance','#16794a'),timesheet:css('--chart-timesheet','#0f6e9e'),approval:css('--chart-approval','#7c3aed'),
        good:css('--good','#16794a'),warning:css('--warn','#946200'),serious:css('--serious','#b45309'),critical:css('--critical','#b42318'),missing:css('--chart-missing','#667085'),
        grid:css('--chart-grid','#d8dde2'),axis:css('--chart-axis','#aeb7c1'),ink:css('--chart-ink','#0f172a'),inkSoft:css('--ink-soft','#475569'),muted:css('--chart-muted','#64748b'),surface:css('--chart-surface','#fff'),tooltip:css('--chart-tooltip','#0f172a'),tooltipText:css('--on-chart-tooltip','#fff'),
        reducedMotion:window.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches||false
    };
    const orange=()=>css('--atlas-orange','#f26a12');

    function ensure(id){
        const el=document.getElementById(id);if(!el)return null;
        let chart=instances.get(id);if(chart&&!chart.isDisposed())return chart;
        chart=echarts.init(el,null,{renderer:'svg'});instances.set(id,chart);
        const ro=new ResizeObserver(()=>chart.resize());ro.observe(el);chart.__atlasResizeObserver=ro;return chart;
    }
    const tip=p=>({backgroundColor:p.tooltip,borderWidth:0,padding:[8,10],textStyle:{color:p.tooltipText,fontSize:12.5,lineHeight:18},extraCssText:'box-shadow:0 8px 22px rgba(0,0,0,.22);border-radius:3px;'});
    const motion=()=>palette().reducedMotion?{animation:false,animationDuration:0,animationDurationUpdate:0}:{animation:true,animationDuration:620,animationEasing:'cubicOut',animationDurationUpdate:260};
    const initials=name=>String(name||'').split(/\s+/).filter(Boolean).slice(0,2).map(x=>x[0].toUpperCase()).join('');
    const bandColor=(band,p)=>{switch(String(band||'').toLowerCase()){case'good':return p.operational;case'warning':return p.warning;case'serious':return p.serious;case'critical':return p.critical;default:return p.operational;}};
    const roleColor=(role,p,index=0)=>{switch(String(role||'').toLowerCase()){case'attendance':return p.attendance;case'timesheet':return p.timesheet;case'approval':return p.approval;case'warning':return p.warning;case'serious':return p.serious;case'critical':return p.critical;default:return p.series[index%p.series.length]||p.operational;}};

    function registerDrilldown(ref){drilldownRef=ref;}
    function drill(name){if(!drilldownRef||!name)return;drilldownRef.invokeMethodAsync('DrilldownTo',name).catch(()=>{});}

    function sparkline(id,values,role='operational'){
        const chart=ensure(id);if(!chart)return;const data=(values||[]).map(v=>v==null?null:Number(v));
        const draw=(silent=false)=>{const p=palette(),color=roleColor(role,p);chart.setOption({...(silent?{animation:false}:motion()),grid:{left:1,right:1,top:3,bottom:3},xAxis:{type:'category',show:false,data:data.map((_,i)=>i)},yAxis:{type:'value',show:false,scale:true},tooltip:{show:false},series:[{type:'line',data,symbol:'none',connectNulls:true,smooth:.22,lineStyle:{color,width:1.8},areaStyle:{color,opacity:.035}}]},true);};
        draw();renderers.set(id,()=>draw(true));
    }

    function performanceField(id,points,xMax,utilizationTarget,medianX,selectedName){
        const chart=ensure(id);if(!chart)return;
        const source=(points||[]).map(x=>({...x,x:+x.x||0,y:+x.y||0,prevX:x.prevX==null?null:+x.prevX,prevY:x.prevY==null?null:+x.prevY,score:+x.score||0,exceptions:+x.exceptions||0,missing:!!x.missing}));
        const draw=(silent=false)=>{
            const p=palette(),selected=String(selectedName||'').toLowerCase();
            const tails=source.filter(x=>x.prevX!=null&&x.prevY!=null&&!x.missing).map((pt,i)=>({name:`move-${i}`,type:'line',silent:true,symbol:['none','circle'],symbolSize:[0,6],data:[[pt.prevX,pt.prevY],[pt.x,pt.y]],lineStyle:{color:String(pt.name).toLowerCase()===selected?orange():p.axis,width:String(pt.name).toLowerCase()===selected?1.8:1,opacity:String(pt.name).toLowerCase()===selected?.95:.52},itemStyle:{color:p.surface,borderColor:String(pt.name).toLowerCase()===selected?orange():p.axis,borderWidth:1.2},z:1}));
            const maxX=Math.max(1,+xMax||Math.max(...source.map(x=>x.x),1)*1.12),target=+utilizationTarget||75,med=+medianX||maxX/2;
            const scatterData=source.map(d=>({...d,value:[d.x,d.y]}));
            chart.setOption({
                ...(silent?{animation:false}:motion()),grid:{left:52,right:22,top:32,bottom:52},
                tooltip:{...tip(p),trigger:'item',formatter:x=>{if(x.seriesName!=='Engineers'||!x.data)return'';const d=x.data;return `<strong>${d.name}</strong><br/>Score <b>${(+d.score).toFixed(1)}</b><br/>Punch hours <b>${(+d.x).toFixed(1)} h</b><br/>Utilization <b>${d.missing?'no monthly source':(+d.y).toFixed(1)+'%'}</b><br/>Exceptions <b>${d.exceptions}</b><br/><span style="opacity:.72">Click to focus across the Atlas</span>`;}},
                xAxis:{type:'value',min:0,max:maxX,name:'ACCOUNTABLE / PUNCH HOURS',nameLocation:'middle',nameGap:32,nameTextStyle:{color:p.inkSoft,fontSize:11,fontWeight:600},axisLine:{lineStyle:{color:p.axis}},axisTick:{show:false},axisLabel:{color:p.inkSoft,fontSize:11.5},splitLine:{show:false}},
                yAxis:{type:'value',min:0,max:110,name:'UTILIZATION (%)',nameLocation:'end',nameGap:14,nameTextStyle:{color:p.inkSoft,fontSize:11,fontWeight:600,align:'left'},axisLine:{show:true,lineStyle:{color:p.axis}},axisTick:{show:false},axisLabel:{color:p.inkSoft,fontSize:11.5},splitLine:{lineStyle:{color:p.grid,type:'dashed'}}},
                series:[
                    ...tails,
                    {name:'zones',type:'line',silent:true,symbol:'none',data:[],markArea:{silent:true,label:{color:p.muted,fontSize:10.5},itemStyle:{borderWidth:0},data:[
                        [{name:'UNDERUSED',xAxis:0,yAxis:target,itemStyle:{color:'rgba(15,95,122,.025)'}},{xAxis:med,yAxis:110}],
                        [{name:'OVERLOADED',xAxis:med,yAxis:target,itemStyle:{color:'rgba(242,106,18,.025)'}},{xAxis:maxX,yAxis:110}],
                        [{name:'INCONSISTENT',xAxis:0,yAxis:0,itemStyle:{color:'rgba(217,45,32,.018)'}},{xAxis:med,yAxis:target}],
                        [{name:'BALANCED',xAxis:med,yAxis:0,itemStyle:{color:'rgba(22,121,74,.018)'}},{xAxis:maxX,yAxis:target}]
                    ]},markLine:{silent:true,symbol:'none',label:{show:false},lineStyle:{color:p.axis,type:'dashed',opacity:.55},data:[{xAxis:med},{yAxis:target}]}},
                    {name:'Engineers',type:'scatter',z:4,data:scatterData,symbol:'circle',symbolSize:(value,params)=>Math.max(22,Math.min(48,18+(+params.data.score||0)*.28)),cursor:'pointer',
                        label:{show:true,formatter:x=>x.data.missing?`{missing|${initials(x.data.name)}}`:`{normal|${initials(x.data.name)}}`,fontWeight:700,fontSize:10.5,rich:{normal:{color:'#fff',fontWeight:700},missing:{color:p.inkSoft,fontWeight:700}}},
                        itemStyle:{color:d=>d.data.missing?p.surface:String(d.data.name).toLowerCase()===selected?orange():bandColor(d.data.band,p),borderColor:d=>d.data.missing?p.missing:String(d.data.name).toLowerCase()===selected?p.surface:p.surface,borderType:d=>d.data.missing?'dashed':'solid',borderWidth:d=>String(d.data.name).toLowerCase()===selected?4:2,shadowBlur:d=>String(d.data.name).toLowerCase()===selected?14:0,shadowColor:'rgba(242,106,18,.28)'},
                        emphasis:{scale:1.15,itemStyle:{borderWidth:3}}
                    }
                ]
            },true);
            chart.off('click');chart.on('click',x=>{if(x.seriesName==='Engineers'&&x.data)drill(x.data.name);});
        };
        draw();renderers.set(id,()=>draw(true));
    }

    function movementRiver(id,categories,series,median,selectedName){
        const chart=ensure(id);if(!chart)return;const rows=series||[],selected=String(selectedName||'').toLowerCase();
        const draw=(silent=false)=>{const p=palette();const rendered=rows.map((s,i)=>{const isSelected=String(s.name||'').toLowerCase()===selected,isAttention=!!s.attention,color=isSelected?orange():isAttention?roleColor(s.role||'operational',p,i):p.axis;return{name:s.name,type:'line',data:s.values||[],smooth:.24,connectNulls:true,showSymbol:false,symbol:'circle',lineStyle:{color,width:isSelected?2.6:isAttention?1.55:.9,opacity:isSelected?1:isAttention?.82:.25},itemStyle:{color},emphasis:{focus:'series',lineStyle:{width:2.4,opacity:1}},endLabel:{show:isSelected||isAttention,formatter:x=>x.value==null?'':`${initials(s.name)}  ${(+x.value).toFixed(0)}`,color,fontSize:11,fontWeight:isSelected?700:600,distance:5},labelLayout:{moveOverlap:'shiftY'},z:isSelected?5:isAttention?3:1};});rendered.push({name:'Team median',type:'line',data:median||[],smooth:.24,showSymbol:false,silent:true,lineStyle:{color:p.ink,width:1.5,type:'dashed',opacity:.78},z:2});chart.setOption({...(silent?{animation:false}:motion()),grid:{left:42,right:76,top:16,bottom:38},tooltip:{...tip(p),trigger:'axis',formatter:params=>`<strong>${params[0]?.axisValueLabel||''}</strong><br/>${params.filter(x=>x.value!=null).sort((a,b)=>b.value-a.value).slice(0,8).map(x=>`${x.marker}${x.seriesName}: <b>${(+x.value).toFixed(1)}</b>`).join('<br/>')}`},xAxis:{type:'category',boundaryGap:false,data:categories||[],axisLine:{lineStyle:{color:p.axis}},axisTick:{show:false},axisLabel:{color:p.inkSoft,fontSize:11,margin:10}},yAxis:{type:'value',min:0,max:100,axisLine:{show:false},axisTick:{show:false},axisLabel:{color:p.inkSoft,fontSize:11},splitLine:{lineStyle:{color:p.grid}}},series:rendered},true);};
        draw();renderers.set(id,()=>draw(true));
    }

    function portraitHistory(id,categories,series){
        const chart=ensure(id);if(!chart)return;const rows=series||[];
        const draw=(silent=false)=>{const p=palette();chart.setOption({...(silent?{animation:false}:motion()),grid:{left:44,right:20,top:30,bottom:36},tooltip:{...tip(p),trigger:'axis'},legend:{top:0,itemWidth:18,itemHeight:2,textStyle:{color:p.inkSoft,fontSize:11}},xAxis:{type:'category',data:categories||[],axisLine:{lineStyle:{color:p.axis}},axisTick:{show:false},axisLabel:{fontSize:11,color:p.inkSoft}},yAxis:{min:0,max:100,axisLine:{show:false},axisTick:{show:false},axisLabel:{fontSize:11,color:p.inkSoft},splitLine:{lineStyle:{color:p.grid}}},series:rows.map((s,i)=>{const color=s.role==='operational'?orange():roleColor(s.role,p,i);return{name:s.name,type:'line',data:s.values||[],connectNulls:true,smooth:.18,symbol:s.role==='operational'?'circle':['diamond','rect','triangle'][i%3],symbolSize:6,lineStyle:{color,width:s.role==='operational'?2.4:1.4,opacity:s.role==='operational'?1:.72},itemStyle:{color}};})},true);};
        draw();renderers.set(id,()=>draw(true));
    }

    function dispose(id){const c=instances.get(id);if(c&&!c.isDisposed())c.dispose();if(c?.__atlasResizeObserver)c.__atlasResizeObserver.disconnect();instances.delete(id);renderers.delete(id);}
    function refresh(){for(const [id,render] of [...renderers]){const c=instances.get(id);if(!c||c.isDisposed()){renderers.delete(id);continue;}try{render();}catch{}}}
    window.addEventListener('epa-theme-changed',refresh);window.addEventListener('epa-motion-changed',refresh);
    window.epaAtlas={registerDrilldown,sparkline,performanceField,movementRiver,portraitHistory,dispose,refreshTheme:refresh};
})();
