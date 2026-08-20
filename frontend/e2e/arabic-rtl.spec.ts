import { expect, test } from '@playwright/test';

import { signIn } from './helpers';

test.describe('Arabic and right-to-left', () => {
  test('switching language flips direction and translates the shell', async ({ page }) => {
    await signIn(page, 'admin');

    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');

    await page.getByRole('button', { name: 'Language' }).click();

    // Direction is set once on the document element; Material reads it from there, which is why nothing else
    // in the application needs to know.
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.locator('html')).toHaveAttribute('lang', 'ar');

    await expect(page.getByText('إدارة المستخدمين')).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'اسم المستخدم' })).toBeVisible();

    // The table still lays out - a right-to-left flip that breaks the grid is the usual failure here.
    await expect(page.locator('tbody tr').first()).toBeVisible();
  });

  test('the language choice survives a reload and reaches the API', async ({ page }) => {
    await signIn(page, 'admin');
    await page.getByRole('button', { name: 'Language' }).click();
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

    await page.reload();

    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

    // A failed sign-in in Arabic should come back in Arabic: the SPA sends Accept-Language, so the API's
    // messages match the interface instead of contradicting it.
    const problem = await page.evaluate(async () => {
      const response = await fetch('/api/auth/login', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json', 'Accept-Language': 'ar' },
        body: JSON.stringify({ username: 'no-such-user-here', password: 'WrongPassword@1' }),
      });

      return response.text();
    });

    expect(problem).toMatch(/[؀-ۿ]/);

    // The machine-readable code is never translated - it is what the SPA and the tests branch on.
    expect(problem).toContain('INVALID_CREDENTIALS');
  });

  test('the interface is usable at a phone width', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await signIn(page, 'admin');

    // The table scrolls inside its own container; the page body must not scroll sideways.
    const bodyOverflows = await page.evaluate(
      () => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
    );

    expect(bodyOverflows).toBe(false);
    await expect(page.locator('tbody tr').first()).toBeVisible();
  });
});
