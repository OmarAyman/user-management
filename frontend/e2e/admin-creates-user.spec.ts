import { expect, test } from '@playwright/test';

import { createUserViaApi, signIn, uniqueName } from './helpers';

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
  test('the edit form arrives populated, with the immutable field locked', async ({ page }) => {
    // The defect this covers shipped: `id` is a signal input bound from the route, and the component read it in
    // its constructor - before Angular assigns route-bound inputs - so the fetch never ran and every field was
    // blank under a correct "Edit user" heading.
    //
    // The browser suite already opened this page to scan it for accessibility violations, and an empty form has
    // none. Only asserting the values catches it.
    await signIn(page, 'admin');

    const username = uniqueName('edit');
    const created = await createUserViaApi(page, username);

    // Straight to the route rather than hunting the row's edit link: the defect was the route parameter
    // arriving after the component was constructed, and this exercises exactly that. Clicking through the list
    // would add ten identically-labelled links to disambiguate for no extra coverage.
    await page.goto(`/users/${created.id}/edit`);

    await expect(page.getByRole('heading', { name: 'Edit user' })).toBeVisible();

    await expect(page.getByLabel('Username')).toHaveValue(username);
    await expect(page.getByLabel('Email')).toHaveValue(`${username}@example.com`);
    await expect(page.getByLabel('First name')).toHaveValue('Smoke');
    await expect(page.getByLabel('Last name')).toHaveValue('Test');

    // Immutable server-side, so the form must not offer it. Enabled and empty was the symptom.
    await expect(page.getByLabel('Username')).toBeDisabled();
  });
});
