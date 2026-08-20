import { expect, test } from '@playwright/test';

import { apiRequest, createUserViaApi, signIn, uniqueName } from './helpers';

test.describe('the read-only role', () => {
  test('sees the list but no mutation controls anywhere', async ({ page }) => {
    await signIn(page, 'readOnly');

    await expect(page.locator('tbody tr').first()).toBeVisible();

    await expect(page.getByRole('link', { name: /Create user/ })).toBeHidden();
    await expect(page.locator('a[href*="/edit"]')).toHaveCount(0);
    await expect(page.locator('button[aria-label^="Delete"]')).toHaveCount(0);
    await expect(page.getByLabel('Show deleted users')).toBeHidden();

    // Admin-only navigation is absent too.
    await expect(page.getByRole('link', { name: 'Audit' })).toBeHidden();
  });

  test('is refused by the API even when the request bypasses the interface', async ({ page }) => {
    await signIn(page, 'admin');
    const target = await createUserViaApi(page, uniqueName('rotarget'));

    await signIn(page, 'readOnly');

    // The point of the whole exercise: hiding a button proves nothing, so the call is made directly with a
    // read-only token. The server, not the UI, is what refuses it.
    const deleteAttempt = await apiRequest(page, 'DELETE', `/api/users/${target.id}`, undefined, 'readOnly');
    expect(deleteAttempt.status).toBe(403);

    const createAttempt = await apiRequest(
      page,
      'POST',
      '/api/users',
      {
        username: uniqueName('forbidden'),
        email: 'forbidden@example.com',
        firstName: 'Should',
        lastName: 'Fail',
        password: 'Created@123456',
        roleId: 2,
      },
      'readOnly',
    );
    expect(createAttempt.status).toBe(403);

    const deletedListing = await apiRequest(page, 'GET', '/api/users/deleted', undefined, 'readOnly');
    expect(deletedListing.status).toBe(403);

    const auditTrail = await apiRequest(page, 'GET', '/api/audit-logs', undefined, 'readOnly');
    expect(auditTrail.status).toBe(403);
  });

  test('can still edit its own profile, which is what read-only means here', async ({ page }) => {
    await signIn(page, 'readOnly');

    await page.getByRole('link', { name: 'Profile' }).click();
    await expect(page.getByRole('heading', { name: 'My profile' })).toBeVisible();

    await page.getByLabel('First name').fill('Read');
    await page.getByRole('button', { name: 'Save', exact: true }).click();

    // "Read-only" refers to other people's data; the assignment's matrix grants self-profile editing to all
    // three roles. There is no role control on this page at all.
    await expect(page.getByText('Profile updated.')).toBeVisible();
    await expect(page.getByLabel('Role')).toBeHidden();
  });
});
