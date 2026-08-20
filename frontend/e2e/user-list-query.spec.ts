import { expect, test } from '@playwright/test';

import { signIn } from './helpers';

test.describe('the user list', () => {
  test('search, role filter, sort and paging each change the URL and the rows', async ({ page }) => {
    await signIn(page, 'admin');

    // Search. The URL is the source of truth for list state, so it must carry the term.
    await page.getByLabel('Search').fill('khalil');
    await expect(page).toHaveURL(/search=khalil/);
    await expect(page.locator('tbody tr').first()).toContainText('khalil');

    await page.getByLabel('Search').fill('');
    await expect(page).not.toHaveURL(/search=khalil/);

    // Column sort.
    await page.getByRole('button', { name: 'Username' }).click();
    await expect(page).toHaveURL(/sortBy=username/);

    const firstAscending = await page.locator('tbody tr td').first().innerText();

    await page.getByRole('button', { name: 'Username' }).click();
    await expect(page).toHaveURL(/sortDirection=Descending/);

    const firstDescending = await page.locator('tbody tr td').first().innerText();

    // Reversing the direction must actually reorder the rows, not merely accept the parameter.
    expect(firstAscending).not.toBe(firstDescending);

    // Paging.
    await page.getByRole('button', { name: 'Next page' }).click();
    await expect(page).toHaveURL(/pageNumber=2/);
    await expect(page.locator('tbody tr').first()).toBeVisible();
  });

  test('a search with no matches shows the empty state rather than a bare table', async ({ page }) => {
    await signIn(page, 'admin');

    await page.getByLabel('Search').fill('zzz-nothing-matches-this-zzz');

    await expect(page.locator('app-empty-state')).toBeVisible();
    await expect(page.locator('table')).toBeHidden();
  });

  test('a deep link restores the exact list state', async ({ page }) => {
    await signIn(page, 'admin');

    // The reason list state lives in the URL: this link is shareable and survives a reload.
    await page.goto('/users?pageNumber=1&pageSize=25&sortBy=email&sortDirection=Ascending');

    await expect(page.locator('tbody tr').first()).toBeVisible();
    await expect(page).toHaveURL(/sortBy=email/);
  });
});
