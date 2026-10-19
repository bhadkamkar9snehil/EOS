const { chromium } = require('playwright');
const path = require('path');

const url = process.argv[2] || 'http://127.0.0.1:8095/';
const out = process.argv[3] || path.join(__dirname, 'shots');
const views = [{ w: 1920, h: 2000 }, { w: 1440, h: 2400 }, { w: 390, h: 5200 }];

(async () => {
  const browser = await chromium.launch({ channel: 'msedge' });
  for (const theme of ['light', 'dark']) {
    for (const v of views) {
      const ctx = await browser.newContext({ viewport: { width: v.w, height: v.h }, colorScheme: theme });
      await ctx.addInitScript(t => { try { localStorage.setItem('eos.theme', t); } catch { } }, theme);
      const page = await ctx.newPage();
      await page.goto(url, { waitUntil: 'networkidle' });
      await page.waitForSelector('.ov section', { timeout: 30000 });
      await page.waitForTimeout(1500);
      const file = path.join(out, `cc-${v.w}-${theme}.png`);
      await page.screenshot({ path: file });
      const boxes = await page.$$eval('.ov section', els => els.map(e => {
        const r = e.getBoundingClientRect();
        const h2 = e.querySelector('h2')?.textContent;
        const over = [...e.querySelectorAll('*')].some(c => c.scrollWidth > c.clientWidth + 1 && getComputedStyle(c).overflowX !== 'visible');
        let used = 0;
        for (const c of e.children) { const b = c.getBoundingClientRect(); used = Math.max(used, b.bottom - r.top); }
        return `${h2}: x${Math.round(r.left)} y${Math.round(r.top)} ${Math.round(r.width)}x${Math.round(r.height)} contentH ${Math.round(used)}${over ? ' scrollX' : ''}`;
      }));
      console.log(`\n${v.w} ${theme}\n  ` + boxes.join('\n  '));
      await ctx.close();
    }
  }
  await browser.close();
})();
