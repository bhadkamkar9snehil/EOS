// Lazy-loads the (1.1MB) echarts.min.js bundle on demand, only on the routes that
// actually render charts. Call epaEchartsLoader.ensureLoaded() from a page's
// OnAfterRenderAsync before any epaCharts.*/epaAtlas.*/epaAnalyticsCharts.* call -
// those wrappers only touch the `echarts` global lazily inside functions invoked
// at chart-draw time, so this just has to win the race before the first draw.
(() => {
    let pending = null;

    function ensureLoaded() {
        if (window.echarts) return Promise.resolve();
        if (pending) return pending;
        pending = new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = '_content/EngineeringPerformance.UI/echarts.min.js';
            script.onload = () => resolve();
            script.onerror = () => {
                pending = null;
                reject(new Error('Failed to load echarts.min.js'));
            };
            document.head.appendChild(script);
        });
        return pending;
    }

    window.epaEchartsLoader = { ensureLoaded };
})();
