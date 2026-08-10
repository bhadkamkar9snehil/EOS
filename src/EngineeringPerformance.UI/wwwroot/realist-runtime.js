(() => {
    function syncGauge() {
        const score = document.querySelector('.pulse-score-main > strong');
        if (!score) return;
        const numeric = Math.max(0, Math.min(100, Number.parseFloat(score.textContent || '0') || 0));
        score.style.setProperty('--gauge-score', `${numeric * 2.7}deg`);
    }

    function start() {
        syncGauge();
        const observer = new MutationObserver(() => requestAnimationFrame(syncGauge));
        observer.observe(document.body, { childList: true, subtree: true, characterData: true });
        window.addEventListener('epa-skin-changed', syncGauge);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
    else start();
})();
