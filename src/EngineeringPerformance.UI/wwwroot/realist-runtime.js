(() => {
    const navOrder = [
        'Overview',
        'Timesheets',
        'Peer Insights',
        'Data Imports',
        'Imported Data',
        'Employees & Teams',
        'Review Templates',
        'Reports',
        'Scoring'
    ];

    function isRealist() {
        return document.documentElement.dataset.skin === 'realist';
    }

    function syncGauge() {
        const gauge = document.querySelector('.pulse-score-main');
        const score = gauge?.querySelector(':scope > strong');
        if (!gauge || !score) return;
        const numeric = Math.max(0, Math.min(100, Number.parseFloat(score.textContent || '0') || 0));
        const sweep = `${numeric * 2.7}deg`;
        gauge.style.setProperty('--gauge-score', sweep);
        score.style.setProperty('--gauge-score', sweep);
    }

    function syncNavOrder() {
        if (!isRealist()) return;
        const nav = document.querySelector('.nav-rail nav');
        if (!nav) return;
        const buttons = Array.from(nav.querySelectorAll(':scope > button'));
        const byLabel = new Map(buttons.map(button => [button.textContent.trim().replace(/\s+/g, ' '), button]));
        for (const label of navOrder) {
            const button = byLabel.get(label);
            if (button) nav.appendChild(button);
        }
    }

    function sync() {
        syncGauge();
        syncNavOrder();
    }

    function start() {
        sync();
        const observer = new MutationObserver(() => requestAnimationFrame(sync));
        observer.observe(document.body, { childList: true, subtree: true, characterData: true });
        window.addEventListener('epa-skin-changed', sync);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
    else start();
})();