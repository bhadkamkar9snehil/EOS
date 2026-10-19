import { expect, Page, test } from '@playwright/test';
import { cell, openGrid, saved, trackErrors, value } from './helpers';

const ID = 'skills-matrix';
test.describe.configure({ mode: 'serial' });

async function firstRatingCell(page: Page) {
  return page.evaluate((id) => {
    const g = window.EosGridHost.get(id);
    const col = g.vcols.find((c: any) => c.type === 'rating');
    const row = g.rows[0];
    return { person: row.cells.name as string, skill: col.title as string, key: col.key as string, current: (row.cells[col.key] ?? null) as number | null };
  }, ID);
}

test('headers show the full skill names, none cut with an ellipsis', async ({ page }) => {
  await openGrid(page, '/skills', ID);
  const titles = await page.locator('.eg-htext').allTextContents();
  expect(titles.length).toBeGreaterThanOrEqual(3);
  expect(titles.some((t) => t.includes('…'))).toBe(false);
  const clipped = await page.evaluate(() => [...document.querySelectorAll('.eg-htext')].filter((e) => e.scrollHeight > e.clientHeight + 1).length);
  expect(clipped).toBe(0);
});

test('a rating typed into a cell is saved to the database, survives reload, and is restored afterwards', async ({ page }) => {
  const errors = trackErrors(page);
  await openGrid(page, '/skills', ID);
  const { person, skill, key, current } = await firstRatingCell(page);
  const next = current === 4 ? 3 : 4;

  const c = await cell(page, ID, person, skill);
  await c.click();
  await page.keyboard.press(String(next));
  await expect.poll(() => value(page, ID, person, key)).toBe(next);
  await saved(page);

  await page.reload();
  await page.waitForFunction((id) => !!window.EosGridHost.get(id), ID);
  const again = await cell(page, ID, person, skill);
  await expect(again.locator(`.eg-pill.r${next}`)).toBeVisible();

  await again.click();
  if (current === null) await page.keyboard.press('Delete');
  else await page.keyboard.press(String(current));
  await saved(page);
  await page.reload();
  await page.waitForFunction((id) => !!window.EosGridHost.get(id), ID);
  expect(await value(page, ID, person, key)).toBe(current);
  expect(errors, errors.join('\n')).toEqual([]);
});

test('Delete un-rates a cell and Undo brings the rating back, both stored in the database', async ({ page }) => {
  await openGrid(page, '/skills', ID);
  const { person, skill, key, current } = await firstRatingCell(page);
  test.skip(current === null, 'the first cell is unrated, nothing to delete');
  const c = await cell(page, ID, person, skill);
  await c.click();
  await page.keyboard.press('Delete');
  await expect.poll(() => value(page, ID, person, key)).toBeNull();
  await saved(page);
  await page.keyboard.press('Control+z');
  await expect.poll(() => value(page, ID, person, key)).toBe(current);
  await saved(page);
  await page.reload();
  await page.waitForFunction((id) => !!window.EosGridHost.get(id), ID);
  expect(await value(page, ID, person, key)).toBe(current);
});

test('an out-of-range rating is refused before it reaches the server', async ({ page }) => {
  await openGrid(page, '/skills', ID);
  const { person, skill } = await firstRatingCell(page);
  const c = await cell(page, ID, person, skill);
  await c.click();
  await page.keyboard.press('9');
  await expect(page.locator('.eg-toast.bad')).toContainText('at most');
});

test('the engineer column stays put while the skill columns scroll sideways', async ({ page }) => {
  await openGrid(page, '/skills', ID);
  const scrollable = await page.evaluate(() => { const s = document.querySelector('.eg-scroller')!; return s.scrollWidth > s.clientWidth; });
  test.skip(!scrollable, 'few skills, nothing to scroll');
  const name = page.locator('.eg-cell.pin').first();
  const before = (await name.boundingBox())!.x;
  await page.evaluate(() => { document.querySelector('.eg-scroller')!.scrollLeft = 400; });
  const after = (await name.boundingBox())!.x;
  expect(Math.abs(after - before)).toBeLessThan(2);
});
