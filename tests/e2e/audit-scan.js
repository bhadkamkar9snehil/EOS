// Runtime audit: overflow, unlabeled controls, contrast, touch targets, headings, console errors.
// Usage: node tests/e2e/audit-scan.js [baseUrl] [outDir]
const { chromium } = require('playwright');
const path = require('path');
const fs = require('fs');

const base = process.argv[2] || 'http://127.0.0.1:8095';
const out = process.argv[3] || path.join(__dirname, 'shots', 'audit');
fs.mkdirSync(out, { recursive: true });
const routes = ['/', '/planner', '/projects', '/projects/201', '/people', '/people/19', '/capacity', '/demand', '/gantt',
  '/calendar', '/skills', '/training', '/reports', '/alerts', '/register', '/whatif', '/imports', '/infrastructure', '/settings'];
const views = [{ w: 1440, h: 900 }, { w: 390, h: 844 }];

function probe() {
  const lum = c => { const m = c.match(/[\d.]+/g); if (!m) return null; const [r, g, b, a = 1] = m.map(Number);
    const f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; };
    return { L: 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b), a }; };
  const bgOf = el => { for (let e = el; e; e = e.parentElement) { const c = getComputedStyle(e).backgroundColor; const l = lum(c); if (l && l.a > 0.9) return l.L; } return 1; };
  const vis = e => { const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && s.display !== 'none' && +s.opacity > 0.3; };
  const lowContrast = [];
  for (const e of document.querySelectorAll('body *')) {
    if (!vis(e) || e.closest('svg')) continue;
    const own = [...e.childNodes].some(n => n.nodeType === 3 && n.textContent.trim().length > 1);
    if (!own) continue;
    const s = getComputedStyle(e); const fg = lum(s.color); if (!fg) continue;
    const bg = bgOf(e); const hi = Math.max(fg.L, bg), lo = Math.min(fg.L, bg);
    const ratio = (hi + 0.05) / (lo + 0.05); const size = parseFloat(s.fontSize); const bold = +s.fontWeight >= 700;
    const need = size >= 24 || (size >= 18.66 && bold) ? 3 : 4.5;
    if (ratio < need && !e.closest('button:disabled, [disabled]')) lowContrast.push(`${e.tagName.toLowerCase()}.${(e.className || '').toString().split(' ')[0]} "${e.textContent.trim().slice(0, 24)}" ${ratio.toFixed(2)}`);
  }
  const unlabeled = [...document.querySelectorAll('button, a[href], input:not([type=hidden]), select, textarea, [role=button], [tabindex="0"]')]
    .filter(vis).filter(e => {
      const name = (e.getAttribute('aria-label') || e.getAttribute('title') || e.getAttribute('aria-labelledby') || (e.labels && e.labels.length ? 'l' : '') || e.textContent || e.getAttribute('placeholder') || '').trim();
      return !name;
    }).map(e => e.outerHTML.slice(0, 90));
  const small = [...document.querySelectorAll('button, a[href], input, select, [role=button]')].filter(vis)
    .filter(e => { const r = e.getBoundingClientRect(); return r.height < 24 || r.width < 24; }).length;
  const de = document.documentElement;
  const wide = [...document.querySelectorAll('body *')].filter(e => { const r = e.getBoundingClientRect(); return vis(e) && r.right > innerWidth + 2 && !e.closest('[style*=overflow], .tablewrap, .gx, .scroller, table'); })
    .slice(0, 4).map(e => `${e.tagName.toLowerCase()}.${(e.className || '').toString().split(' ')[0]} right=${Math.round(e.getBoundingClientRect().right)}`);
  return { hscroll: de.scrollWidth > innerWidth + 1, wide, h1: document.querySelectorAll('h1').length, main: !!document.querySelector('main, [role=main]'),
    unlabeled: unlabeled.slice(0, 6), unlabeledN: unlabeled.length, small, lowN: lowContrast.length, low: [...new Set(lowContrast)].slice(0, 6) };
}

(async () => {
  const browser = await chromium.launch({ channel: 'msedge' });
  const report = [];
  for (const theme of ['light', 'dark']) for (const v of views) {
    const ctx = await browser.newContext({ viewport: { width: v.w, height: v.h }, colorScheme: theme });
    await ctx.addInitScript(t => { try { localStorage.setItem('eos.theme', t); } catch { } }, theme);
    for (const r of routes) {
      const page = await ctx.newPage(); const errs = [];
      page.on('console', m => { if (m.type() === 'error') errs.push(m.text().slice(0, 120)); });
      page.on('pageerror', e => errs.push(e.message.slice(0, 120)));
      try { await page.goto(base + r, { waitUntil: 'networkidle', timeout: 30000 }); } catch (e) { errs.push('nav ' + e.message.slice(0, 60)); }
      await page.waitForTimeout(1200);
      const res = await page.evaluate(probe).catch(e => ({ error: e.message }));
      report.push({ route: r, theme, w: v.w, errs: errs.slice(0, 3), ...res });
      if (theme === 'light' && v.w === 1440 || theme === 'dark' && v.w === 390)
        await page.screenshot({ path: path.join(out, `${r.replace(/\W+/g, '_') || 'home'}-${v.w}-${theme}.png`) });
      await page.close();
    }
    await ctx.close();
  }
  await browser.close();
  fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify(report, null, 1));
  for (const x of report) console.log(`${x.route} ${x.theme} ${x.w}: hscroll=${x.hscroll} h1=${x.h1} main=${x.main} unlabeled=${x.unlabeledN} small=${x.small} lowContrast=${x.lowN} errs=${x.errs.length}`);
})();
