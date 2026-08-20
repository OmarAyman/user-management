import { Page, expect, test } from '@playwright/test';

import { signIn } from './helpers';

/**
 * Layout verification at the four widths the brief cares about.
 *
 * The assertion that matters is the same everywhere: the page body must not scroll horizontally. Wide content
 * is allowed - the users table is deliberately wider than a phone - but it has to scroll inside its own
 * container rather than pushing the document sideways, because a page that pans horizontally is unusable on
 * touch and disorienting with a mouse.
 */
const VIEWPORTS = [
  { name: 'phone', width: 375, height: 812 },
  { name: 'tablet portrait', width: 768, height: 1024 },
  { name: 'tablet landscape', width: 1024, height: 768 },
  { name: 'laptop', width: 1440, height: 900 },
] as const;

/** True when the document itself pans sideways. One pixel of tolerance for sub-pixel rounding. */
async function bodyOverflowsHorizontally(page: Page): Promise<boolean> {
  return page.evaluate(
    () => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
  );
}

/** Elements sticking out past the viewport, ignoring anything inside a deliberate scroll container. */
async function elementsOutsideViewport(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const limit = document.documentElement.clientWidth;
    const offenders: string[] = [];

    for (const element of document.querySelectorAll('body *')) {
      const rect = element.getBoundingClientRect();

      if (rect.width === 0 || rect.height === 0) {
        continue;
      }

      // Content inside an overflow container is allowed to be wider than the screen: that is the point of it.
      const scroller = element.closest('.table-scroll, .cdk-virtual-scroll-viewport, mat-dialog-container');

      if (scroller !== null) {
        continue;
      }

      if (rect.right > limit + 1 || rect.left < -1) {
        offenders.push(`${element.tagName.toLowerCase()}.${(element.className || '').toString().split(' ')[0]}`);
      }
    }

    return [...new Set(offenders)].slice(0, 8);
  });
}

for (const viewport of VIEWPORTS) {
  test.describe(`${viewport.name} (${viewport.width}x${viewport.height})`, () => {
    test.use({ viewport: { width: viewport.width, height: viewport.height } });

    test('sign-in fits the viewport', async ({ page }) => {
      await page.goto('/login');
      await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();

      expect(await bodyOverflowsHorizontally(page)).toBe(false);
      expect(await elementsOutsideViewport(page)).toEqual([]);
    });

    test('the users page, its toolbar and its filters all fit', async ({ page }) => {
      await signIn(page, 'admin');
      await expect(page.locator('tbody tr').first()).toBeVisible();

      expect(await bodyOverflowsHorizontally(page)).toBe(false);
      expect(await elementsOutsideViewport(page)).toEqual([]);

      // Navigation has to be reachable at every width, whether inline or behind the menu button.
      const inlineNav = page.locator('.shell-nav a').first();
      const navToggle = page.locator('.shell-nav-toggle');

      const reachable = (await inlineNav.isVisible()) || (await navToggle.isVisible());
      expect(reachable, 'navigation must be reachable').toBe(true);

      // The controls that drive the list must be usable, not merely present.
      await expect(page.getByLabel('Search')).toBeVisible();
      await expect(page.getByLabel('Role')).toBeVisible();
    });

    test('the table scrolls inside its own container rather than the page', async ({ page }) => {
      await signIn(page, 'admin');
      await expect(page.locator('tbody tr').first()).toBeVisible();

      const scroller = page.locator('.table-scroll');
      await expect(scroller).toBeVisible();

      const contained = await scroller.evaluate((element) => {
        const style = getComputedStyle(element);

        return style.overflowX === 'auto' || style.overflowX === 'scroll';
      });

      expect(contained).toBe(true);
      expect(await bodyOverflowsHorizontally(page)).toBe(false);
    });

    test('pagination controls are visible and usable', async ({ page }) => {
      await signIn(page, 'admin');

      await expect(page.locator('mat-paginator')).toBeVisible();
      await expect(page.getByRole('button', { name: 'Next page' })).toBeVisible();

      await page.getByRole('button', { name: 'Next page' }).click();
      await expect(page).toHaveURL(/pageNumber=2/);

      expect(await bodyOverflowsHorizontally(page)).toBe(false);
    });

    test('the create form fits and stays submittable', async ({ page }) => {
      await signIn(page, 'admin');
      await page.goto('/users/new');

      await expect(page.getByLabel('Username')).toBeVisible();
      await expect(page.getByRole('button', { name: 'Save' })).toBeVisible();

      expect(await bodyOverflowsHorizontally(page)).toBe(false);
      expect(await elementsOutsideViewport(page)).toEqual([]);
    });

    test('the confirmation dialog stays inside the viewport', async ({ page }) => {
      await signIn(page, 'admin');
      await page.locator('button[aria-label^="Delete"]').first().click();

      const dialog = page.getByRole('dialog');
      await expect(dialog).toBeVisible();

      const box = await dialog.boundingBox();
      expect(box).not.toBeNull();
      expect(box!.width).toBeLessThanOrEqual(viewport.width);
      expect(box!.height).toBeLessThanOrEqual(viewport.height);

      // Scoped to the dialog: "Delete" also names the button on every table row, and an unscoped lookup
      // resolves to eleven elements.
      //
      // Both actions must be reachable - a dialog whose confirm button is off-screen is a dead end.
      await expect(dialog.getByRole('button', { name: 'Cancel' })).toBeVisible();
      await expect(dialog.getByRole('button', { name: 'Delete' })).toBeVisible();

      await page.keyboard.press('Escape');
    });

    test('the profile page fits', async ({ page }) => {
      await signIn(page, 'admin');
      await page.goto('/profile');

      await expect(page.getByRole('heading', { name: 'My profile' })).toBeVisible();

      expect(await bodyOverflowsHorizontally(page)).toBe(false);
      expect(await elementsOutsideViewport(page)).toEqual([]);
    });

    test('the audit page fits', async ({ page }) => {
      await signIn(page, 'admin');
      await page.goto('/audit');

      await expect(page.getByRole('heading', { name: 'Audit trail' })).toBeVisible();

      expect(await bodyOverflowsHorizontally(page)).toBe(false);
    });

    test('Arabic right-to-left does not break the layout', async ({ page }) => {
      await signIn(page, 'admin');
      await page.getByRole('button', { name: 'Language' }).click();
      await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

      await expect(page.locator('tbody tr').first()).toBeVisible();

      // Mirroring is where a layout built with left/right offsets falls apart, so it is checked at every width.
      expect(await bodyOverflowsHorizontally(page)).toBe(false);
      expect(await elementsOutsideViewport(page)).toEqual([]);
    });
  });
}
