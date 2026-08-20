import { expect, test } from '@playwright/test';

import { Credentials, signIn } from './helpers';

test.describe('sign-in', () => {
  test('an administrator signs in and lands on the user list', async ({ page }) => {
    await signIn(page, 'admin');

    await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible();
    await expect(page.getByText('admin')).toBeVisible();

    // The audit link is Admin-only, so its presence confirms the role reached the UI.
    await expect(page.getByRole('link', { name: 'Audit' })).toBeVisible();
  });

  test('wrong credentials are refused inline, not as a toast that can be missed', async ({ page }) => {
    await page.goto('/login');
    await page.getByLabel('Username').fill(Credentials.admin.username);
    await page.getByLabel('Password', { exact: true }).fill('DefinitelyWrong@1');
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();

    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page).toHaveURL(/\/login/);
  });

  test('the session survives a full page reload', async ({ page }) => {
    await signIn(page, 'admin');

    // The access token lives only in memory, so surviving this proves the httpOnly refresh cookie is doing its
    // job - the entire reason for that design.
    await page.reload();

    await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible();

    const storage = await page.evaluate(() =>
      JSON.stringify({ local: Object.keys(localStorage), session: Object.keys(sessionStorage) }),
    );

    expect(storage).not.toContain('accessToken');
    expect(storage).not.toContain('token');
  });
});
