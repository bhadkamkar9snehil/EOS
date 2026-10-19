import { expect, test } from '@playwright/test';
import { cell, openGrid, saved, trackErrors, value } from './helpers';

const ID = 'skills-matrix';
test.describe.configure({ mode: 'serial' });

async function firstUnrated(page: import('@playwright/test').Page) {
  return page.evaluate((id) => {
    const g = window.EosGridHost.get(id);
    for (const row of g.rows) {
      for (const c of g.vcols) {
        if (c.type === 'rating' && (row.cells[c.key] === null || row.cells[c.key] === undefined)) return { person: row.cells.name, skill: c.title, key: c.key };
      }
    }
    return null;
  }, ID);
}

test('headers show the full skill names, none cut with an ellipsis', async ({ page }) => {
  await openGrid(page, '/skills', ID);
  const titles = await page.locator('.eg-htext').allTextContents();
  expect(titles.length).toBeGreaterThan(5);
  expect(titles.some((t) => t.includes('…'))).toBe(false);
  const clipped = await page.evaluate(() => [...document.querySelectorAll('.eg-htext')].filter((e) => e.scrollHeight > e.clientHeight + 1).length);
  expect(clipped).toBe(0);
});

test('a rating typed into a cell is saved to the database, survives reload, and Delete removes it', async ({ page }) => {
  const errors = trackErrors(page);
  await openGrid(page, '/skills', ID);
  const target = await firstUnrated(page);
  test.skip(!target, 'every cell already has a rating');
  const { person, skill, key } = target!;
  const c = await cell(page, ID, person, skill);
  await c.click();
  await page.keyboard.press('4');
  await expect.poll(() => value(page, ID, person, key)).toBe(4);
  await saved(page);

  await page.reload();
  await page.waitForFunction((id) => !!window.EosGridHost.get(id), ID);
  const again = await cell(page, ID, person, skill);
  await expect(again.locator('.eg-pill.r4')).toBeVisible();

  await again.click();
  await page.keyboard.press('Delete');
  await saved(page);
  await page.reload();
  await page.waitForFunction((id) => !!window.EosGridHost.get(id), ID);
  const cleared = await cell(page, ID, person, skill);
  await expect(cleared.locator('.eg-pill')).toHaveCount(0);
  expect(errors, errors.join('\n')).toEqual([]);
});

test('an out-of-range rating is refused before it reaches the server', async ({ page }) => {
  await openGrid(page, '/skills', ID);
  const target = await firstUnrated(page);
  test.skip(!target, 'every cell already has a rating');
  const c = await cell(page, ID, target!.person, target!.skill);
  await c.click();
  await page.keyboard.press('9');
  await expect(page.locator('.eg-toast.bad')).toContainText('at most');
});

test('the engineer column stays put while the skill columns scroll sideways', async ({ page }) => {
  await openGrid(page, '/skills', ID);
  const name = page.locator('.eg-cell.pin').first();
  const before = (await name.boundingBox())!.x;
  await page.evaluate(() => { document.querySelector('.eg-scroller')!.scrollLeft = 400; });
  const after = (await name.boundingBox())!.x;
  expect(Math.abs(after - before)).toBeLessThan(2);
});
