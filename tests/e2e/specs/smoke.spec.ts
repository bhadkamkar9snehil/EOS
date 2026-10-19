import { expect, test } from '@playwright/test';
import { trackErrors } from './helpers';

const pages = [
  '/', '/planner', '/projects', '/gantt', '/calendar', '/capacity', '/whatif', '/demand', '/people', '/skills',
  '/training', '/infrastructure', '/register', '/alerts', '/reports', '/imports', '/settings', '/grid-lab',
];

for (const url of pages) {
  test(`page ${url} loads without errors`, async ({ page }) => {
    const errors = trackErrors(page);
    await page.goto(url);
    await expect(page.locator('main')).toBeVisible();
    await expect(page.locator('main')).not.toContainText('Loading…', { timeout: 15_000 });
    await expect(page.locator('#blazor-error-ui')).toBeHidden();
    await page.waitForTimeout(400);
    expect(errors, errors.join('\n')).toEqual([]);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1);
    expect(overflow, 'page scrolls sideways').toBe(false);
  });
}

test('health endpoint reports ok', async ({ request }) => {
  const r = await request.get('/health');
  expect(r.ok()).toBe(true);
  expect((await r.json()).status).toBe('ok');
});
