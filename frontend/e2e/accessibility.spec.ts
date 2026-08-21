import AxeBuilder from '@axe-core/playwright';
import { Page, expect, test } from '@playwright/test';

import { signIn, uniqueName } from './helpers';

/**
 * Automated accessibility audit.
 *
 * axe-core cannot prove an interface is accessible - it catches the machine-checkable subset: missing
 * accessible names, unassociated labels, insufficient contrast, duplicate ids, broken landmark and table
 * semantics, invalid ARIA. Those are exactly the defects that survive a visual review, which is why they are
 * worth automating; the rest (keyboard order, focus behaviour, whether a message makes sense read aloud) is
 * covered by the explicit tests below and by a manual pass recorded in docs/16-accessibility-audit.md.
 *
 * WCAG 2.1 A and AA are the tags scanned. Violations are asserted at every impact level rather than filtered
 * to "serious and critical": on a surface this small there is no reason to carry known minor breakage.
 */
async function scan(page: Page, context?: string) {
  // Settle before scanning. Angular renders Material form fields in two steps - the control first, its label
  // association a tick later - so a scan that lands between them reports violations that do not exist a frame
  // later. Two profile scans failed exactly that way under load while passing in isolation; a flaky
  // accessibility gate is worse than none, because it teaches everyone to ignore it.
  await page.waitForLoadState('networkidle');
  await page.evaluate(() => new Promise((resolve) => requestAnimationFrame(() => resolve(null))));

  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze();

  const summary = results.violations.map((violation) => ({
    id: violation.id,
    impact: violation.impact,
    nodes: violation.nodes.length,
    help: violation.help,
    targets: violation.nodes.slice(0, 3).map((node) => node.target.join(' ')),
  }));

  expect(summary, `axe violations${context === undefined ? '' : ` (${context})`}`).toEqual([]);
}

test.describe('accessibility - English', () => {
  test('sign-in page', async ({ page }) => {
    await page.goto('/login');
    await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();

    await scan(page, 'login');
  });

  test('sign-in page with a validation failure showing', async ({ page }) => {
    await page.goto('/login');

    // Submitting empty triggers the required-field errors, which have to be announced and associated with
    // their inputs rather than merely coloured red.
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    await expect(page.locator('mat-error').first()).toBeVisible();

    await scan(page, 'login with validation errors');
  });

  test('sign-in page with an authentication failure showing', async ({ page }) => {
    await page.goto('/login');
    await page.getByLabel('Username').fill('nobody-at-all');
    await page.getByLabel('Password', { exact: true }).fill('WrongPassword@1');
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();

    await expect(page.getByRole('alert')).toBeVisible();

    await scan(page, 'login with an auth failure');
  });

  test('users list', async ({ page }) => {
    await signIn(page, 'admin');
    await expect(page.locator('tbody tr').first()).toBeVisible();

    await scan(page, 'users list');
  });

  test('users list showing the empty state', async ({ page }) => {
    await signIn(page, 'admin');
    await page.getByLabel('Search').fill('zzz-nothing-matches-zzz');
    await expect(page.locator('app-empty-state')).toBeVisible();

    await scan(page, 'users list empty state');
  });

  test('users list showing deleted users', async ({ page }) => {
    await signIn(page, 'admin');
    await page.getByLabel('Show deleted users').check();
    await expect(page).toHaveURL(/deleted=true/);

    await scan(page, 'deleted users list');
  });

  test('create user form', async ({ page }) => {
    await signIn(page, 'admin');
    await page.goto('/users/new');
    await expect(page.getByRole('heading', { name: 'Create user' })).toBeVisible();

    await scan(page, 'create user');
  });

  test('create user form with validation errors showing', async ({ page }) => {
    await signIn(page, 'admin');
    await page.goto('/users/new');

    await page.getByLabel('Username').fill('a');
    await page.getByLabel('Email').fill('not-an-email');
    await page.getByLabel('Password', { exact: true }).fill('short');
    await page.getByLabel('First name').click();

    await expect(page.locator('mat-error').first()).toBeVisible();

    await scan(page, 'create user with errors');
  });

  test('edit user form', async ({ page }) => {
    await signIn(page, 'admin');

    await page.locator('a[href*="/edit"]').first().click();
    await expect(page.getByRole('heading', { name: 'Edit user' })).toBeVisible();

    await scan(page, 'edit user');
  });

  test('confirmation dialog', async ({ page }) => {
    await signIn(page, 'admin');

    await page.locator('button[aria-label^="Delete"]').first().click();
    await expect(page.getByRole('dialog')).toBeVisible();

    // A dialog is where ARIA most often goes wrong: no accessible name, no modal semantics, focus left behind.
    await scan(page, 'confirm dialog');
  });

  test('profile page', async ({ page }) => {
    await signIn(page, 'admin');
    await page.getByRole('link', { name: 'Profile' }).click();
    await expect(page.getByRole('heading', { name: 'My profile' })).toBeVisible();

    await scan(page, 'profile');
  });

  test('audit page', async ({ page }) => {
    await signIn(page, 'admin');
    await page.getByRole('link', { name: 'Audit' }).click();
    await expect(page.getByRole('heading', { name: 'Audit trail' })).toBeVisible();

    await scan(page, 'audit');
  });

  test('forbidden page', async ({ page }) => {
    await signIn(page, 'user');
    await page.goto('/audit');
    await expect(page.getByRole('heading', { name: 'Not permitted' })).toBeVisible();

    await scan(page, 'forbidden');
  });
});

test.describe('accessibility - Arabic right-to-left', () => {
  test('users list in Arabic', async ({ page }) => {
    await signIn(page, 'admin');
    await page.getByRole('button', { name: 'Language' }).click();
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

    // Mirroring a layout can break label association and reading order, so the RTL pass is not redundant.
    await scan(page, 'users list, Arabic');
  });

  test('create user form in Arabic', async ({ page }) => {
    await signIn(page, 'admin');
    await page.getByRole('button', { name: 'Language' }).click();
    await page.goto('/users/new');
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

    await scan(page, 'create user, Arabic');
  });

  test('profile in Arabic', async ({ page }) => {
    await signIn(page, 'admin');
    await page.getByRole('button', { name: 'Language' }).click();
    await page.goto('/profile');
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

    await scan(page, 'profile, Arabic');
  });

  test('sign-in page in Arabic', async ({ page }) => {
    await page.goto('/login');
    await page.getByRole('button', { name: /العربية/ }).click();
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

    await scan(page, 'login, Arabic');
  });
});

test.describe('accessibility - behaviours axe cannot check', () => {
  test('the whole sign-in form is reachable and submittable by keyboard alone', async ({ page }) => {
    await page.goto('/login');

    // Wait for the form before tabbing. `goto` resolves on load, which is not the same as the application
    // having bootstrapped - tabbing into a page that has not rendered its form sends every keystroke nowhere,
    // and the failure then looks like a broken sign-in rather than a test racing the app. It became visible
    // when the stylesheet grew and first paint moved later.
    await expect(page.getByLabel('Username')).toBeVisible();

    await page.keyboard.press('Tab');
    await page.keyboard.type('admin');
    await page.keyboard.press('Tab');
    await page.keyboard.type('Admin@123456');

    // Tab past the password-reveal button to the submit button, then activate it with the keyboard.
    await page.keyboard.press('Tab');
    await page.keyboard.press('Tab');
    await page.keyboard.press('Enter');

    await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible();
  });

  test('keyboard focus is visibly indicated on a text field', async ({ page }) => {
    await page.goto('/login');

    // Driven from a known element with a real key press: `:focus-visible` deliberately does not match
    // programmatic focus, so calling element.focus() and then asserting on the outline tests nothing.
    await page.getByLabel('Username').press('Tab');

    const indicator = await page.evaluate(() => {
      const active = document.activeElement;

      if (active === null) {
        return null;
      }

      const style = getComputedStyle(active);

      return {
        tag: active.tagName,
        outlineStyle: style.outlineStyle,
        outlineWidth: style.outlineWidth,
        boxShadow: style.boxShadow,
        matchesFocusVisible: active.matches(':focus-visible'),
      };
    });

    expect(indicator).not.toBeNull();
    expect(indicator?.matchesFocusVisible).toBe(true);

    // A Material text input carries no outline of its own: the surrounding form field indicates focus. With
    // the outlined appearance used here that means the `mat-focused` state - which recolours the notched
    // outline - and the label floating above the field. Asserting an outline on the input itself would fail
    // while the field is plainly focused on screen, and asserting a line ripple would too: that belongs to the
    // fill appearance, not this one.
    await expect(page.locator('mat-form-field.mat-focused')).toHaveCount(1);
    await expect(page.locator('mat-form-field.mat-focused .mdc-floating-label--float-above')).toHaveCount(1);
  });

  test('keyboard focus on a button shows the focus ring', async ({ page }) => {
    await page.goto('/login');

    // The regression guard for a defect this audit found: Material sets `outline: none` on buttons, and a bare
    // `:focus-visible` rule loses to it on specificity, so the documented ring was not rendering at all. This
    // measures the computed outline rather than trusting the stylesheet.
    await page.getByLabel('Password', { exact: true }).press('Tab');
    await page.keyboard.press('Tab');

    const outline = await page.evaluate(() => {
      const active = document.activeElement;

      if (active === null) {
        return null;
      }

      const style = getComputedStyle(active);

      return {
        tag: active.tagName,
        focusVisible: active.matches(':focus-visible'),
        outlineStyle: style.outlineStyle,
        outlineWidth: style.outlineWidth,
      };
    });

    expect(outline?.focusVisible).toBe(true);
    expect(outline?.outlineStyle, `no outline on ${outline?.tag}`).not.toBe('none');
    expect(outline?.outlineWidth).not.toBe('0px');
  });

  test('the password toggle has a distinct accessible name from the field', async ({ page }) => {
    await page.goto('/login');

    // The defect this guards against: the button once carried aria-label="Password", giving a screen reader two
    // controls with the same name and no indication of what the button does.
    await expect(page.getByRole('button', { name: 'Show password' })).toBeVisible();
    await page.getByRole('button', { name: 'Show password' }).click();
    await expect(page.getByRole('button', { name: 'Hide password' })).toBeVisible();
  });

  test('the confirmation dialog traps focus and returns it to the trigger', async ({ page }) => {
    await signIn(page, 'admin');

    const trigger = page.locator('button[aria-label^="Delete"]').first();
    const triggerLabel = await trigger.getAttribute('aria-label');
    await trigger.click();

    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();

    // Focus moves after the open animation, so this polls rather than sampling once - the earlier version of
    // this test read activeElement too early and reported a defect that was not there.
    //
    // The dialog container itself receives focus (autoFocus: 'dialog'), which is the right choice for a
    // destructive confirmation: a screen reader reads the whole question, and Enter does not land on Delete.
    await expect
      .poll(() => page.evaluate(() => document.activeElement?.closest('[role="dialog"]') !== null))
      .toBe(true);

    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();

    // ...and comes back to the control that opened it, so a keyboard user does not lose their place.
    await expect
      .poll(() => page.evaluate(() => document.activeElement?.getAttribute('aria-label')))
      .toBe(triggerLabel);
  });

  test('a validation error is announced and tied to its input', async ({ page }) => {
    await signIn(page, 'admin');
    await page.goto('/users/new');

    await page.getByLabel('Email').fill('not-an-email');
    await page.getByLabel('First name').click();

    const describedBy = await page.getByLabel('Email').getAttribute('aria-describedby');
    expect(describedBy).toBeTruthy();

    // The message must be the element the input points at, not merely red text somewhere nearby.
    const errorId = await page.locator('mat-error').first().getAttribute('id');
    expect(describedBy).toContain(errorId ?? 'missing');

    await expect(page.getByLabel('Email')).toHaveAttribute('aria-invalid', 'true');
  });

  test('the users table exposes real table semantics', async ({ page }) => {
    await signIn(page, 'admin');

    const table = page.getByRole('table');
    await expect(table).toBeVisible();

    // Sortable columns are buttons inside header cells, and the sort state is announced through aria-sort.
    await expect(page.getByRole('columnheader').first()).toBeVisible();
    await page.getByRole('button', { name: 'Username' }).click();

    await expect(page.locator('th[aria-sort]').first()).toBeVisible();
  });

  test('the document has one main landmark and a labelled navigation region', async ({ page }) => {
    await signIn(page, 'admin');

    await expect(page.getByRole('main')).toHaveCount(1);
    await expect(page.getByRole('navigation', { name: 'Menu' })).toBeVisible();
  });

  test('the page has no duplicate element ids', async ({ page }) => {
    await signIn(page, 'admin');
    await expect(page.locator('tbody tr').first()).toBeVisible();

    const duplicates = await page.evaluate(() => {
      const seen = new Map<string, number>();

      for (const element of document.querySelectorAll('[id]')) {
        seen.set(element.id, (seen.get(element.id) ?? 0) + 1);
      }

      return [...seen.entries()].filter(([, count]) => count > 1).map(([id]) => id);
    });

    expect(duplicates).toEqual([]);
  });

  test('a created user is announced through a live region', async ({ page }) => {
    await signIn(page, 'admin');
    const username = uniqueName('a11y');

    await page.goto('/users/new');
    await page.getByLabel('Username').fill(username);
    await page.getByLabel('Email').fill(`${username}@example.com`);
    await page.getByLabel('First name').fill('Access');
    await page.getByLabel('Last name').fill('Ible');
    await page.getByLabel('Password', { exact: true }).fill('Created@123456');
    await page.getByRole('button', { name: 'Save' }).click();

    // Material's snack bar renders into an aria-live container, so success is not conveyed by colour alone.
    await expect(page.locator('[aria-live]').filter({ hasText: 'User created.' })).toBeVisible();
  });
});
