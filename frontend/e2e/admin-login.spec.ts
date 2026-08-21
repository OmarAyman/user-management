import { expect, test } from '@playwright/test';

import { createUserViaApi, signIn, uniqueName } from './helpers';

test.describe('sign-in', () => {
  test('an administrator signs in and lands on the user list', async ({ page }) => {
    await signIn(page, 'admin');

    await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible();
    await expect(page.getByText('admin')).toBeVisible();

    // The audit link is Admin-only, so its presence confirms the role reached the UI.
    await expect(page.getByRole('link', { name: 'Audit' })).toBeVisible();
  });

  test('wrong credentials are refused inline, not as a toast that can be missed', async ({ page }) => {
    // A throwaway account, not the shared admin one. Five wrong passwords lock an account for fifteen minutes,
    // so a test that spends the admin account's lockout budget poisons every other test that signs in as
    // admin - not on one run, but on the fifth run inside the window. That is exactly how it failed: repeated
    // runs against a long-lived containerised stack locked admin out and 37 tests went red at once, with
    // nothing in the failures pointing at the cause.
    //
    // The goto comes first because the helper calls the API with fetch from the page context, and a relative
    // URL on about:blank goes nowhere.
    await page.goto('/login');

    const victim = uniqueName('lockme');
    await createUserViaApi(page, victim);

    await page.getByLabel('Username').fill(victim);
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
