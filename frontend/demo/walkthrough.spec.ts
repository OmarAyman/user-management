import { expect, Page, test } from '@playwright/test';

import { caption, clearOverlays, detail, showUrl } from './overlay';

/**
 * The walkthrough from docs/14-demo-script.md, recorded.
 *
 * Beat for beat in the script's order, including its one deliberate deviation - soft delete before the audit
 * trail, so the audit screen shows a lifecycle the viewer just watched happen rather than an empty table.
 *
 * It asserts as it goes, but the assertions are there to keep the recording honest rather than to test
 * anything: if a beat cannot happen, the run fails instead of producing a video of a broken demo.
 */

const ADMIN = { username: 'admin', password: 'Admin@123456' };
const READONLY = { username: 'readonly', password: 'ReadOnly@1234' };

/**
 * The language toggle, in either direction.
 *
 * It is an icon button, so its accessible name comes entirely from `aria-label` - which is localized:
 * "Language" in English and the Arabic word for it once switched. Matching on the visible "English" text
 * cost a seven-minute timeout, because there is no visible text at all.
 */
const LANGUAGE_BUTTON = /Language|اللغة/;

/** Unique per run, so a second recording does not collide with the first one's user. */
const NEW_USER = `demo${Date.now().toString().slice(-6)}`;

async function signIn(page: Page, who: { username: string; password: string }): Promise<void> {
  await page.goto('/login');
  await expect(page.getByLabel('Username')).toBeVisible();
  await page.getByLabel('Username').fill(who.username);
  await page.getByLabel('Password', { exact: true }).fill(who.password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
}

test('user management walkthrough', async ({ page }) => {
  // ------------------------------------------------------------------ sign in
  await page.goto('/login');
  await expect(page.getByLabel('Username')).toBeVisible();
  await caption(page, 'Sign in. Labels, a password reveal, validation on submit.', 2400);

  await page.getByLabel('Username').fill('no-such-user');
  await page.getByLabel('Password', { exact: true }).fill('WrongPassword@1');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('alert')).toBeVisible();

  await caption(
    page,
    'An unknown username, a wrong password and a <b>deleted account</b> all get this same answer, so sign-in '
      + 'cannot be used to discover whether an account exists.',
    4200,
  );

  await clearOverlays(page);
  await signIn(page, ADMIN);
  await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible();

  // ------------------------------------------------------------------ the list, and where the token lives
  await caption(page, 'The user list: newest first, with role and status per row.', 2400);

  const storage = await page.evaluate(() =>
    Object.keys(localStorage)
      .map((key) => `${key} = ${localStorage.getItem(key) ?? ''}`)
      .join('\n'),
  );

  await detail(page, `Object.keys(localStorage)\n${storage}`, 1200);
  await caption(
    page,
    'Local storage holds one key: the language. The <b>access token is held in memory only</b>, so an injected '
      + 'script cannot read it out later.',
    4200,
  );
  await clearOverlays(page);

  // ------------------------------------------------------------------ create a user
  await page.getByRole('link', { name: /Create user/ }).click();
  await expect(page.getByLabel('Username')).toBeVisible();
  await caption(page, 'Creating a user. The username is immutable once set, and the password policy is stated up front.', 3200);

  // Uniqueness on blur: 'admin' is taken, and the field says so before anything is submitted.
  await page.getByLabel('Username').fill('admin');
  await page.getByLabel('Email').click();
  await page.waitForTimeout(1200);
  await caption(
    page,
    'Uniqueness is checked <b>on blur</b>, not on every keystroke, and the database is still the authority when '
      + 'the form is submitted.',
    3800,
  );
  await clearOverlays(page);

  await page.getByLabel('Username').fill(NEW_USER);
  await page.getByLabel('Email').fill(`${NEW_USER}@example.com`);
  await page.getByLabel('First name').fill('Demo');
  await page.getByLabel('Last name').fill('Account');
  await page.getByLabel('Password', { exact: true }).fill('Created@123456');
  await page.getByRole('button', { name: 'Save' }).click();

  await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible();
  await caption(page, 'Created, and announced through a live region rather than by colour alone.', 2600);
  await clearOverlays(page);

  // ------------------------------------------------------------------ search, sort, empty state
  await page.getByLabel('Search').fill(NEW_USER);
  await page.waitForTimeout(1000);
  await showUrl(page, 2600);
  await caption(
    page,
    'Search is debounced: one request, not one per keystroke. The <b>URL carries the list state</b>, so a '
      + 'filtered view is shareable and the back button works.',
    4200,
  );

  await page.getByLabel('Search').fill('');
  await page.waitForTimeout(800);
  await page.getByRole('columnheader', { name: /Username/i }).click();
  await page.waitForTimeout(700);
  await showUrl(page, 2400);
  await clearOverlays(page);

  await page.getByLabel('Search').fill('zzzzz');
  await page.waitForTimeout(1100);
  await caption(page, 'No matches shows an empty state, not a blank table.', 2600);
  await page.getByLabel('Search').fill('');
  await page.waitForTimeout(900);
  await clearOverlays(page);

  // ------------------------------------------------------------------ soft delete and restore
  // Before the audit trail on purpose, so the audit screen shows this lifecycle rather than an empty one.
  await page.getByLabel('Search').fill(NEW_USER);
  await page.waitForTimeout(1100);

  const row = page.getByRole('row', { hasText: NEW_USER });
  await row.getByRole('button', { name: 'Delete' }).click();

  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await caption(page, 'The confirmation names the consequence: the history is kept, and an administrator can restore them.', 3400);
  await dialog.getByRole('button', { name: 'Delete' }).click();
  await page.waitForTimeout(1200);

  await caption(page, 'Gone from the list, but <b>no user is ever physically deleted</b> through the API.', 3200);

  await page.getByLabel('Show deleted users').check();
  await page.waitForTimeout(1400);
  await caption(page, 'Deleted users, with when and by whom. That route is Admin-only, and the server enforces it.', 3400);

  const deletedRow = page.getByRole('row', { hasText: NEW_USER });
  await deletedRow.getByRole('button', { name: 'Restore' }).click();

  const restoreDialog = page.getByRole('dialog');

  if (await restoreDialog.isVisible().catch(() => false)) {
    await restoreDialog.getByRole('button', { name: 'Restore' }).click();
  }

  await page.waitForTimeout(1600);
  await page.getByLabel('Show deleted users').uncheck();
  await page.waitForTimeout(1200);
  await caption(
    page,
    'Restored. Deleting the same user twice answers <b>409</b>, not 404: an administrator needs to tell '
      + '"already gone" from "never existed".',
    4000,
  );
  await clearOverlays(page);

  // ------------------------------------------------------------------ audit trail
  await page.getByRole('link', { name: 'Audit' }).click();
  await page.waitForTimeout(1600);
  await caption(page, 'The lifecycle just performed, newest first: created, deleted, restored.', 3400);
  await caption(
    page,
    'The target column carries the username <b>and the user id</b>. The id is the identity: usernames are '
      + 'released when a user is deleted, so the trail stays unambiguous if the name is reused later.',
    4600,
  );
  await clearOverlays(page);

  // ------------------------------------------------------------------ role-based authorization
  await page.goto('/login');
  await page.waitForTimeout(600);
  await signIn(page, READONLY);
  await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible();
  await caption(page, 'Signed in as a read-only user: no create, no edit, no delete, and no Audit link.', 3600);

  const forbidden = await page.evaluate(async () => {
    const response = await fetch('/api/audit-logs', { credentials: 'include' });

    return `GET /api/audit-logs\n\nHTTP ${response.status}\n${(await response.text()).slice(0, 200)}`;
  });

  await detail(page, forbidden, 1500);
  await caption(
    page,
    'Hiding a button is courtesy. The <b>server refuses the same operation</b>, and the suite proves it for all '
      + 'three roles across every mutating endpoint.',
    4600,
  );
  await clearOverlays(page);

  // ------------------------------------------------------------------ profile
  await page.getByRole('link', { name: 'Profile' }).click();
  await page.waitForTimeout(1400);
  await caption(
    page,
    'A user edits their own profile. There is <b>no role control</b>: the profile model has no role field at '
      + 'all, so self-elevation is not something the interface declines, it is something the contract cannot express.',
    5000,
  );
  await clearOverlays(page);

  // ------------------------------------------------------------------ Arabic and RTL
  await page.getByRole('button', { name: LANGUAGE_BUTTON }).click();
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await caption(
    page,
    'Arabic, right to left. Direction is set once on the document and Material reads it from there, so overlays, '
      + 'the paginator and the sort arrows mirror without any per-component work.',
    4600,
  );

  await detail(page, `document.title\n${await page.title()}`, 2400);
  await caption(page, 'Even the browser tab title follows the language.', 2600);
  await clearOverlays(page);

  await page.getByRole('button', { name: LANGUAGE_BUTTON }).click();
  await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');

  // ------------------------------------------------------------------ the API on its own
  const problem = await page.evaluate(async () => {
    const response = await fetch('/api/users?sortBy=passwordHash', { credentials: 'include' });

    return `GET /api/users?sortBy=passwordHash\n\nHTTP ${response.status}\n${await response.text()}`;
  });

  await detail(page, problem, 1500);
  await caption(
    page,
    'A structured error: a stable <b>errorCode</b> the SPA and the tests branch on, a localized sentence, and a '
      + 'trace id that also appears in the logs. Sorting is a whitelist, so a column name from a client never '
      + 'reaches SQL.',
    5200,
  );
  await clearOverlays(page);

  // The URL the README hands the evaluator, not the page it redirects to - so the recording exercises the
  // documented link, including its 301.
  await page.goto('http://localhost:5080/swagger');
  await page.waitForLoadState('networkidle');
  await caption(page, 'And the API stands on its own: Swagger, with the bearer scheme wired up.', 4000);
  await page.waitForTimeout(1500);
});
