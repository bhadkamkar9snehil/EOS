import { expect, test } from '@playwright/test';

test('the rail button switches to dark, the choice survives reload, and switches back', async ({ page }) => {
  await page.goto('/projects');
  const bg = () => page.evaluate(() => getComputedStyle(document.body).backgroundColor);
  await page.evaluate(() => localStorage.removeItem('eos.theme'));
  await page.emulateMedia({ colorScheme: 'light' });
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  const light = await bg();
  await page.locator('.rail-theme').click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  expect(await bg()).not.toBe(light);
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await page.locator('.rail-theme').click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  expect(await bg()).toBe(light);
});

test('with no saved choice the system colour scheme decides', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.goto('/projects');
  await page.evaluate(() => localStorage.removeItem('eos.theme'));
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
});

test('the grid follows the theme', async ({ page }) => {
  await page.goto('/grid-lab');
  await page.evaluate(() => localStorage.setItem('eos.theme', 'dark'));
  await page.reload();
  await expect(page.locator('.eg-row').first()).toBeVisible();
  const panel = await page.evaluate(() => getComputedStyle(document.querySelector('.eg')!).backgroundColor);
  expect(panel).toBe('rgb(23, 26, 32)');
});
