// Renders README screenshots: node readme-shots.js http://127.0.0.1:8095 ../../docs/img
const { chromium } = require('playwright');
const fs = require('fs');
const base = process.argv[2] || 'http://127.0.0.1:8095';
const out = process.argv[3] || '../../docs/img';
const pages = [['command-center', '/'], ['projects', '/projects'], ['gantt', '/gantt'], ['people', '/people'],
  ['capacity', '/capacity'], ['skills', '/skills'], ['whatif', '/whatif'], ['infrastructure', '/infrastructure'],
  ['reports', '/reports'], ['settings', '/settings']];
(async () => {
  fs.mkdirSync(out, { recursive: true });
  const browser = await chromium.launch({ channel: 'msedge' });
  for (const theme of ['light', 'dark']) {
    const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, colorScheme: theme });
    await ctx.addInitScript(t => { try { localStorage.setItem('eos.theme', t); } catch { } }, theme);
    const page = await ctx.newPage();
    for (const [name, path] of pages) {
      await page.goto(base + path, { waitUntil: 'networkidle' });
      await page.waitForTimeout(1500);
      await page.screenshot({ path: `${out}/${name}-${theme}.png` });
      console.log(name, theme);
    }
    await ctx.close();
  }
  await browser.close();
})();
