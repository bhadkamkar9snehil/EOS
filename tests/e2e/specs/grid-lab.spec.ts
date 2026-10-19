import { expect, test } from '@playwright/test';
import { cell, openGrid, saved, value } from './helpers';

const TREE = 'grid-lab-tree';
const FLAT = 'grid-lab-flat';

test.describe('tree grid', () => {
  test.beforeEach(async ({ page }) => { await openGrid(page, '/grid-lab', TREE); });

  test('typing a number saves it and the server recomputes the variance and phase subtotal', async ({ page }) => {
    const before = await value(page, TREE, 'Design', 'hours');
    const c = await cell(page, TREE, 'Design task 1', 'Planned hours');
    await c.click();
    await page.keyboard.type('25.5');
    await page.keyboard.press('Enter');
    await expect.poll(() => value(page, TREE, 'Design task 1', 'hours')).toBe(25.5);
    await saved(page);
    expect(await value(page, TREE, 'Design', 'hours')).not.toBe(before);
    expect(await value(page, TREE, 'Design task 1', 'variance')).not.toBeNull();
  });

  test('text in a number cell is refused and the editor stays open', async ({ page }) => {
    const c = await cell(page, TREE, 'Design task 2', 'Planned hours');
    await c.click();
    await page.keyboard.type('abc');
    await page.keyboard.press('Enter');
    await expect(page.locator('.eg-editor.bad')).toBeVisible();
    await expect(page.locator('.eg-toast')).toContainText('not a number');
    await page.keyboard.press('Escape');
    await expect(page.locator('.eg-editor')).toHaveCount(0);
  });

  test('a finish date before the start is rejected by the server and the old value returns', async ({ page }) => {
    const old = await value(page, TREE, 'Design task 3', 'finish');
    const c = await cell(page, TREE, 'Design task 3', 'Finish');
    await c.click();
    await page.keyboard.type('01-01-2019');
    await page.keyboard.press('Enter');
    await expect(page.locator('.eg-toast.bad')).toContainText('before the start');
    expect(await value(page, TREE, 'Design task 3', 'finish')).toBe(old);
    await expect(c).toHaveClass(/err/);
  });

  test('select cell: type-ahead then Enter picks the option', async ({ page }) => {
    const c = await cell(page, TREE, 'Design task 4', 'Owner');
    await c.click();
    await page.keyboard.press('Enter');
    await expect(page.locator('.eg-list')).toBeVisible();
    await page.keyboard.type('chi');
    await page.keyboard.press('Enter');
    await expect.poll(() => value(page, TREE, 'Design task 4', 'owner')).toBe('Chirag Dhimmar');
  });

  test('Tab commits and moves to the next cell', async ({ page }) => {
    const c = await cell(page, TREE, 'Design task 5', 'Notes');
    await c.click();
    await page.keyboard.type('needs review');
    await page.keyboard.press('Tab');
    await expect.poll(() => value(page, TREE, 'Design task 5', 'notes')).toBe('needs review');
  });

  test('Ctrl+Z undoes and the toast Undo button does the same', async ({ page }) => {
    const old = await value(page, TREE, 'Design task 6', 'hours');
    const c = await cell(page, TREE, 'Design task 6', 'Planned hours');
    await c.click();
    await page.keyboard.type('77');
    await page.keyboard.press('Enter');
    await expect.poll(() => value(page, TREE, 'Design task 6', 'hours')).toBe(77);
    await page.locator('.eg-tundo').click();
    await expect.poll(() => value(page, TREE, 'Design task 6', 'hours')).toBe(old);
    await c.click();
    await page.keyboard.press('Control+y');
    await expect.poll(() => value(page, TREE, 'Design task 6', 'hours')).toBe(77);
    await page.keyboard.press('Control+z');
    await expect.poll(() => value(page, TREE, 'Design task 6', 'hours')).toBe(old);
  });

  test('Excel-style paste fills a block and one undo reverts all of it', async ({ page }) => {
    const c = await cell(page, TREE, 'Build task 1', 'Planned hours');
    const a1 = await value(page, TREE, 'Build task 1', 'hours');
    const a2 = await value(page, TREE, 'Build task 2', 'hours');
    await page.evaluate(() => navigator.clipboard.writeText('10\t5\n11\t6'));
    await c.click();
    await page.keyboard.press('Control+v');
    await expect.poll(() => value(page, TREE, 'Build task 2', 'actual')).toBe(6);
    expect(await value(page, TREE, 'Build task 1', 'hours')).toBe(10);
    expect(await value(page, TREE, 'Build task 2', 'hours')).toBe(11);
    await page.keyboard.press('Control+z');
    await expect.poll(() => value(page, TREE, 'Build task 1', 'hours')).toBe(a1);
    expect(await value(page, TREE, 'Build task 2', 'hours')).toBe(a2);
  });

  test('copy puts a tab-separated block on the real clipboard', async ({ page }) => {
    const c1 = await cell(page, TREE, 'Test task 1', 'Planned hours');
    await c1.click();
    await page.keyboard.press('Shift+ArrowDown');
    await page.keyboard.press('Shift+ArrowRight');
    await page.keyboard.press('Control+c');
    const text = await page.evaluate(() => navigator.clipboard.readText());
    expect(text.split('\n')).toHaveLength(2);
    expect(text.split('\n')[0].split('\t')).toHaveLength(2);
  });

  test('Delete clears a cell, locked phase cells refuse edits with a reason', async ({ page }) => {
    const c = await cell(page, TREE, 'Test task 2', 'Notes');
    await c.click();
    await page.keyboard.type('x');
    await page.keyboard.press('Enter');
    await expect.poll(() => value(page, TREE, 'Test task 2', 'notes')).toBe('x');
    await c.click();
    await page.keyboard.press('Delete');
    await expect.poll(() => value(page, TREE, 'Test task 2', 'notes')).toBeNull();
    const locked = await cell(page, TREE, 'Test', 'Owner');
    await locked.click();
    await page.keyboard.press('Enter');
    await expect(page.locator('.eg-toast.bad')).toContainText('cannot be edited on this row');
  });

  test('space toggles a checkbox and a digit sets a rating', async ({ page }) => {
    const b = await cell(page, TREE, 'Deploy task 1', 'Billable');
    const before = await value(page, TREE, 'Deploy task 1', 'billable');
    await b.click({ position: { x: 3, y: 3 } });
    await page.keyboard.press('Space');
    await expect.poll(() => value(page, TREE, 'Deploy task 1', 'billable')).toBe(!before);
    const p = await cell(page, TREE, 'Deploy task 2', 'Priority');
    await p.click();
    await page.keyboard.press('3');
    await expect.poll(() => value(page, TREE, 'Deploy task 2', 'priority')).toBe(3);
    await page.keyboard.press('7');
    await expect(page.locator('.eg-toast.bad')).toContainText('at most 3');
  });

  test('clicking the twist collapses a phase and the keyboard expands it again', async ({ page }) => {
    const rows = () => page.evaluate((id) => window.EosGridHost.get(id).display.length, TREE);
    const total = await rows();
    await page.locator('.eg-twist').first().click();
    await expect.poll(rows).toBeLessThan(total);
    await page.keyboard.press('Alt+ArrowRight');
    await expect.poll(rows).toBe(total);
  });

  test('every header title is fully visible and column resize persists after reload', async ({ page }) => {
    const clipped = await page.evaluate(() => [...document.querySelectorAll('.eg-htext')].filter((e) => e.scrollHeight > e.clientHeight + 1).length);
    expect(clipped).toBe(0);
    const handle = page.locator('.eg-resize').nth(3);
    const box = (await handle.boundingBox())!;
    const before = await page.evaluate((id) => window.EosGridHost.get(id).vcols[3].width, TREE);
    await page.mouse.move(box.x + 3, box.y + 10);
    await page.mouse.down();
    await page.mouse.move(box.x + 63, box.y + 10, { steps: 6 });
    await page.mouse.up();
    const after = await page.evaluate((id) => window.EosGridHost.get(id).vcols[3].width, TREE);
    expect(after).toBeGreaterThan(before + 40);
    await page.reload();
    await page.waitForFunction((id) => !!window.EosGridHost.get(id), TREE);
    expect(await page.evaluate((id) => window.EosGridHost.get(id).vcols[3].width, TREE)).toBe(after);
    await page.locator('.eg-btn[data-act="reset"]').click();
    expect(await page.evaluate((id) => window.EosGridHost.get(id).vcols[3].width, TREE)).toBe(before);
  });
});

test.describe('flat grid', () => {
  test.beforeEach(async ({ page }) => {
    await openGrid(page, '/grid-lab', TREE);
    await page.getByRole('button', { name: 'Flat, groupable' }).click();
    await page.waitForFunction((id) => !!window.EosGridHost.get(id), FLAT);
    await expect(page.locator('.eg-row').first()).toBeVisible();
  });

  test('grouping by status shows group rows with totals and collapses with Enter', async ({ page }) => {
    await page.locator('.eg-groupsel select').selectOption('status');
    const distinct = await page.evaluate((id) => new Set(window.EosGridHost.get(id).rows.map((r: any) => r.cells.status)).size, FLAT);
    await expect.poll(() => page.evaluate((id) => window.EosGridHost.get(id).display.filter((d: any) => d.kind === 'group').length, FLAT)).toBe(distinct);
    await expect(page.locator('.eg-grp').first()).toBeVisible();
    const n = () => page.evaluate((id) => window.EosGridHost.get(id).display.filter((d: any) => d.kind === 'row').length, FLAT);
    const all = await n();
    await page.locator('.eg-grp').first().locator('.eg-gl').click();
    await page.keyboard.press('Enter');
    await expect.poll(n).toBeLessThan(all);
    await page.keyboard.press('Enter');
    await expect.poll(n).toBe(all);
  });

  test('column menu sorts and filters, chips remove the filter, search narrows rows', async ({ page }) => {
    const header = page.locator('.eg-hcell[role="columnheader"]', { hasText: 'Status' });
    await header.hover();
    await header.locator('.eg-hmenu').click();
    await page.locator('.eg-menu label.eg-mrow', { hasText: 'Delayed' }).locator('input').check();
    await expect(page.locator('.eg-fchip')).toContainText('Status');
    const rows = () => page.evaluate((id) => window.EosGridHost.get(id).display.every((d: any) => d.row.cells.status === 'late'), FLAT);
    await expect.poll(rows).toBe(true);
    await page.keyboard.press('Escape');
    await page.locator('.eg-fchip [data-fx]').click();
    await expect(page.locator('.eg-fchip')).toHaveCount(0);
    await page.locator('.eg-search input').fill('Build task 3');
    await expect.poll(() => page.evaluate((id) => window.EosGridHost.get(id).display.length, FLAT)).toBe(1);
    await page.locator('.eg-search input').fill('zzzz');
    await expect(page.locator('[data-clear-all]')).toBeVisible();
    await page.locator('[data-clear-all]').click();
    await expect.poll(() => page.evaluate((id) => window.EosGridHost.get(id).display.length, FLAT)).toBe(36);
  });

  test('header click sorts, third click clears, density buttons change row height', async ({ page }) => {
    const h = () => page.locator('.eg-hcell[role="columnheader"]', { hasText: 'Planned hours' });
    await h().click();
    await expect(h()).toHaveAttribute('aria-sort', 'ascending');
    await h().click();
    await expect(h()).toHaveAttribute('aria-sort', 'descending');
    await h().click();
    await expect(h()).toHaveAttribute('aria-sort', 'none');
    await page.locator('[data-den="compact"]').click();
    expect(await page.evaluate((id) => window.EosGridHost.get(id).rowH, FLAT)).toBe(28);
    await page.locator('[data-den="roomy"]').click();
    expect(await page.evaluate((id) => window.EosGridHost.get(id).rowH, FLAT)).toBe(42);
  });

  test('CSV export downloads the visible rows', async ({ page }) => {
    const [download] = await Promise.all([page.waitForEvent('download'), page.locator('[data-act="export"]').click()]);
    expect(download.suggestedFilename()).toContain('grid-lab');
  });
});
