(() => {
  const root = document.documentElement;
  let observer;

  const isRealist = () => root.dataset.skin === 'realist';
  const qs = (s, p = document) => p.querySelector(s);

  function click(sel) {
    const el = qs(sel);
    if (el && !el.disabled) el.click();
  }

  function ensureHeaderBank() {
    const topbar = qs('.topbar');
    if (!topbar || qs('.realist-control-bank', topbar)) return;
    const bank = document.createElement('div');
    bank.className = 'realist-control-bank';
    bank.innerHTML = `
      <div class="rcb-month" aria-label="Reporting month">
        <span class="rcb-icon">▣</span>
        <button type="button" class="rcb-month-prev" aria-label="Previous month">‹</button>
        <strong class="rcb-month-label">Month</strong>
        <button type="button" class="rcb-month-next" aria-label="Next month">›</button>
      </div>
      <button type="button" class="rcb-selector" data-rcb="team"><span>Team:</span><strong>All</strong><i>⌄</i></button>
      <button type="button" class="rcb-selector" data-rcb="group"><span>Group:</span><strong>All</strong><i>⌄</i></button>
      <label class="rcb-search"><span>⌕</span><input type="search" placeholder="Search employees..." aria-label="Search employees" /></label>
      <button type="button" class="rcb-rotary" aria-label="Focus employee search"><i></i></button>`;
    const actions = qs('.topbar-actions', topbar);
    topbar.insertBefore(bank, actions || null);

    const input = qs('.rcb-search input', bank);
    input?.addEventListener('focus', () => {
      if (!qs('.global-search-popover')) click('.global-search .icon-button');
    });
    input?.addEventListener('input', e => {
      if (!qs('.global-search-popover')) click('.global-search .icon-button');
      setTimeout(() => {
        const target = qs('.global-search-field input');
        if (target) {
          target.value = e.target.value;
          target.dispatchEvent(new Event('input', { bubbles: true }));
        }
      }, 0);
    });
    qs('.rcb-month-prev', bank)?.addEventListener('click', () => click('.month-control button:first-of-type'));
    qs('.rcb-month-next', bank)?.addEventListener('click', () => click('.month-control button:last-of-type'));
    qs('.rcb-rotary', bank)?.addEventListener('click', () => qs('.rcb-search input', bank)?.focus());
    qs('[data-rcb="team"]', bank)?.addEventListener('click', () => showHint('Team scope: All'));
    qs('[data-rcb="group"]', bank)?.addEventListener('click', () => showHint('Group scope: All'));
    syncHeaderBank();
  }

  function syncHeaderBank() {
    const label = qs('.rcb-month-label');
    const month = qs('.month-control strong');
    if (label && month) label.textContent = month.textContent.trim();
  }

  function showHint(text) {
    let hint = qs('.realist-hint');
    if (!hint) {
      hint = document.createElement('div');
      hint.className = 'realist-hint';
      document.body.appendChild(hint);
    }
    hint.textContent = text;
    hint.classList.add('show');
    clearTimeout(hint.__timer);
    hint.__timer = setTimeout(() => hint.classList.remove('show'), 1200);
  }

  function ensureLowerConsole() {
    const rail = qs('.nav-rail');
    if (!rail || qs('.realist-lower-console', rail)) return;
    const panel = document.createElement('div');
    panel.className = 'realist-lower-console';
    panel.innerHTML = `
      <button class="realist-back" type="button" aria-label="Back to overview"><span>≪</span></button>
      <button class="realist-dial" type="button" aria-label="Switch to Minimal interface"><i></i></button>`;
    const footer = qs('.nav-footer', rail);
    rail.insertBefore(panel, footer || null);
    qs('.realist-back', panel)?.addEventListener('click', () => click('.nav-rail nav button'));
    qs('.realist-dial', panel)?.addEventListener('click', () => window.epaSkin?.apply?.('minimal'));
  }

  function instrumentScore() {
    const score = qs('.pulse-score-main > strong');
    if (!score) return;
    const numeric = Math.max(0, Math.min(100, parseFloat(score.textContent) || 0));
    score.style.setProperty('--gauge-score', `${numeric * 3.6}deg`);
    score.setAttribute('data-gauge', String(Math.round(numeric)));
  }

  function decorateFooter() {
    const footer = qs('.workspace > footer');
    if (!footer || qs('.realist-footer-mark', footer)) return;
    const mark = document.createElement('span');
    mark.className = 'realist-footer-mark';
    mark.textContent = 'EOS · v2.6.1';
    footer.appendChild(mark);
  }

  function decoratePanels() {
    document.querySelectorAll('.performance-field,.attention-lens,.movement-river,.operational-fingerprint,.atlas-pulse,.workbench-header,.timesheet-pulse,.timesheet-ledger,.people-command,.people-roster,.peer-matrix-wrap,.settings-card,.appearance-control,.export-lane,.template-lane,.primary-pane,.activity-pane').forEach(el => {
      if (!el.querySelector(':scope > .realist-fasteners')) {
        const f = document.createElement('i');
        f.className = 'realist-fasteners';
        f.setAttribute('aria-hidden', 'true');
        el.prepend(f);
      }
    });
  }

  function sync() {
    ensureHeaderBank();
    ensureLowerConsole();
    instrumentScore();
    decorateFooter();
    decoratePanels();
    syncHeaderBank();
  }

  function start() {
    sync();
    observer = new MutationObserver(() => requestAnimationFrame(sync));
    observer.observe(document.body, { childList: true, subtree: true, characterData: true });
    window.addEventListener('epa-skin-changed', sync);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
  else start();
})();
