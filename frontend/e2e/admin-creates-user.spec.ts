import { expect, test } from '@playwright/test';

import { signIn, uniqueName } from './helpers';

test.describe('creating a user', () => {
  test('an administrator fills the form and the user appears in the list', async ({ page }) => {
    await signIn(page, 'admin');

    const username = uniqueName('smoke');

    await page.getByRole('link', { name: /Create user/ }).click();
    await expect(page.getByRole('heading', { name: 'Create user' })).toBeVisible();

    await page.getByLabel('Username').fill(username);
    await page.getByLabel('Email').fill(`${username}@example.com`);
    await page.getByLabel('First name').fill('Smoke');
    await page.getByLabel('Last name').fill('Test');
    await page.getByLabel('Password', { exact: true }).fill('Created@123456');

    await page.getByRole('button', { name: 'Save' }).click();

    // Back on the list, and the new user is findable through search - which also exercises the search path.
    await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible();
    await page.getByLabel('Search').fill(username);

    await expect(page.getByText(username, { exact: true })).toBeVisible();
  });

  test('the form refuses a password that fails the policy', async ({ page }) => {
    await signIn(page, 'admin');

    await page.goto('/users/new');

    await page.getByLabel('Username').fill(uniqueName('weak'));
    await page.getByLabel('Email').fill('weak@example.com');
    await page.getByLabel('First name').fill('Weak');
    await page.getByLabel('Last name').fill('Password');
    await page.getByLabel('Password', { exact: true }).fill('short');

    // Blur so the control is touched and its error renders.
    await page.getByLabel('Last name').click();

    await expect(page.getByText(/At least 12 characters/)).toBeVisible();
  });
});
