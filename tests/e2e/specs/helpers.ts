import { expect, Locator, Page } from '@playwright/test';

declare global {
  interface Window { EosGridHost: { get(id: string): any }; }
}

export async function openGrid(page: Page, url: string, gridId: string) {
  await page.goto(url);
  await page.waitForFunction((id) => !!window.EosGridHost && !!window.EosGridHost.get(id), gridId);
  await expect(page.locator('.eg-row').first()).toBeVisible();
}

export async function cell(page: Page, gridId: string, rowLabel: string, colTitle: string): Promise<Locator> {
  const pos = await page.evaluate(([id, row, col]) => {
    const g = window.EosGridHost.get(id);
    const lc = g.vcols.find((c: any) => c.rowLabel) || g.vcols[0];
    const r = g.display.findIndex((d: any) => d.kind === 'row' && String(d.row.cells[lc.key]) === row);
    const c = g.vcols.findIndex((x: any) => (x.title || x.key) === col);
    if (r >= 0 && c >= 0) g.ensureVisible(r, c);
    return { r, c };
  }, [gridId, rowLabel, colTitle]);
  if (pos.r < 0 || pos.c < 0) throw new Error(`No cell for row "${rowLabel}" column "${colTitle}" in ${gridId}`);
  const loc = page.locator(`.eg-cell[data-r="${pos.r}"][data-c="${pos.c}"]`);
  await expect(loc).toBeVisible();
  return loc;
}

export async function value(page: Page, gridId: string, rowLabel: string, key: string) {
  return page.evaluate(([id, row, k]) => {
    const g = window.EosGridHost.get(id);
    const lc = g.vcols.find((c: any) => c.rowLabel) || g.vcols[0];
    const r = g.rows.find((x: any) => String(x.cells[lc.key]) === row);
    return r ? r.cells[k] : undefined;
  }, [gridId, rowLabel, key]);
}

export async function saved(page: Page) {
  await expect(page.locator('.eg-st.ok')).toBeVisible();
}

export function trackErrors(page: Page) {
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push('pageerror: ' + e.message));
  page.on('console', (m) => {
    if (m.type() === 'error' && !/favicon/i.test(m.text())) errors.push('console: ' + m.text());
  });
  page.on('requestfailed', (r) => {
    if (!/favicon|_blazor\/disconnect/i.test(r.url())) errors.push('requestfailed: ' + r.url() + ' ' + (r.failure()?.errorText ?? ''));
  });
  page.on('response', (r) => {
    if (r.status() >= 500) errors.push('http ' + r.status() + ': ' + r.url());
  });
  return errors;
}
