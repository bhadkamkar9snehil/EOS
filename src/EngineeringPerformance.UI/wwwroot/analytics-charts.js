(() => {
    const charts = () => window.epaCharts;

    function weightedStack(id, categories, series, totals, suffix) {
        const api = charts();
        const chart = api?.ensure(id);
        if (!chart) return;

        const optionFor = p => ({
            tooltip: {
                backgroundColor:p.tooltip,borderWidth:0,padding:[9,12],
                textStyle:{color:p.tooltipText,fontSize:12.5,lineHeight:18},
                extraCssText:'box-shadow:0 8px 20px rgba(10,16,28,.24);border-radius:6px;'
            },
            legend:{textStyle:{fontSize:11.5,color:p.inkSoft}},
            xAxis:{axisLabel:{fontSize:11,color:p.inkSoft},splitLine:{lineStyle:{color:p.grid}}},
            yAxis:{axisLabel:{fontSize:11.5,color:p.ink}},
            series:series.map((item,index)=>({itemStyle:{color:api.roleColor(item.role,index,p)}}))
        });

        const p = api.palette();
        chart.clear();
        chart.setOption({
            animation:!p.reducedMotion,
            animationDuration:p.reducedMotion?0:520,
            animationEasing:'cubicOut',
            grid:{left:168,right:58,top:40,bottom:28,containLabel:false},
            legend:{top:0,itemWidth:11,itemHeight:8,itemGap:18,textStyle:{fontSize:11.5,color:p.inkSoft}},
            tooltip:{
                ...optionFor(p).tooltip,
                trigger:'axis',axisPointer:{type:'shadow'},
                formatter:params=>{
                    const index=params[0]?.dataIndex??0;
                    const rows=params.filter(x=>x.value!=null&&Number(x.value)>0).map(x=>`${x.marker}${x.seriesName}: <b>${Number(x.value).toFixed(1)}${suffix||''}</b>`);
                    rows.push(`<span style="opacity:.72">Operational score:</span> <b>${Number(totals[index]||0).toFixed(1)}</b>`);
                    return `<strong>${categories[index]}</strong><br/>${rows.join('<br/>')}`;
                }
            },
            xAxis:{type:'value',min:0,max:100,axisLine:{show:false},axisTick:{show:false},axisLabel:{fontSize:11,color:p.inkSoft,formatter:'{value}'},splitLine:{lineStyle:{color:p.grid}}},
            yAxis:{type:'category',inverse:true,data:categories,axisLine:{show:false},axisTick:{show:false},axisLabel:{fontSize:11.5,color:p.ink,width:154,overflow:'truncate'}},
            series:series.map((item,index)=>({
                name:item.name,type:'bar',stack:'operational',barWidth:15,emphasis:{focus:'series'},
                itemStyle:{color:api.roleColor(item.role,index,p),borderRadius:index===0?[3,0,0,3]:index===series.length-1?[0,3,3,0]:0},
                label:index===series.length-1?{show:true,position:'right',distance:8,color:p.ink,fontSize:11.5,fontWeight:650,formatter:x=>Number(totals[x.dataIndex]||0).toFixed(1)}:{show:false},
                data:item.values
            }))
        }, true);

        api.registerThemeUpdater(id, next => chart.setOption({animationDurationUpdate:0,...optionFor(next)}, {lazyUpdate:true,silent:true}));
    }

    window.epaAnalyticsCharts={weightedStack};
})();
