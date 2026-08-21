import { expect, test } from '@playwright/test';

/**
 * What the page loads, and what it sends back.
 *
 * Both of these were defects rather than hypotheticals. The interface fetched Roboto and the Material icon
 * font from Google, so on a network that cannot reach fonts.gstatic.com every icon rendered as its own
 * ligature text - the toolbar read "visibility", "delete", "edit". And the document itself carried no security
 * headers at all: the API set a Content-Security-Policy on its JSON, which is the one response that cannot
 * execute script, while the page that can had none.
 */
test.describe('page assets and headers', () => {
  test('loads nothing from a third-party origin', async ({ page }) => {
    const requested: string[] = [];

    page.on('request', (request) => requested.push(request.url()));

    await page.goto('/login');
    await expect(page.getByLabel('Username')).toBeVisible();
    await page.waitForLoadState('networkidle');

    // The origin is read after navigating, not inside the handler: when the first request fires the page is
    // still on about:blank, so comparing against page.url() at that moment counts the application's own
    // document as third-party. That is how the first version of this test failed.
    const own = new URL(page.url()).origin;

    const external = requested.filter((url) => /^https?:/.test(url) && new URL(url).origin !== own);

    // Fonts are bundled. A CDN that is slow, blocked or gone must not change what the interface looks like.
    expect(external).toEqual([]);
  });

  test('renders Material icons as glyphs, not as their ligature text', async ({ page }) => {
    await page.goto('/login');

    await expect(page.getByLabel('Username')).toBeVisible();

    const measurements = await page.evaluate(async () => {
      // Ask for the face explicitly rather than trusting fonts.ready: a font is fetched lazily when something
      // needs a glyph from it, so `ready` can resolve while the icon font is still on its way. The first
      // version of this test checked availability alone and failed against a build where the icons were fine.
      await document.fonts.load("24px 'Material Icons'");
      await document.fonts.ready;

      function widthOf(family: string): number {
        const probe = document.createElement('span');
        probe.style.cssText =
          `position:absolute;visibility:hidden;font-size:24px;line-height:1;white-space:nowrap;` +
          `font-family:${family};font-feature-settings:'liga';`;
        probe.textContent = 'visibility';
        document.body.appendChild(probe);
        const width = Math.round(probe.getBoundingClientRect().width);
        probe.remove();

        return width;
      }

      return {
        iconFontLoaded: document.fonts.check("24px 'Material Icons'"),
        withIconFont: widthOf("'Material Icons'"),
        withFallback: widthOf('Arial'),
      };
    });

    expect(measurements.iconFontLoaded).toBe(true);

    // The word collapses to one glyph when the ligature resolves: 24px against roughly 80px of text. Checking
    // the font is merely available would pass while every icon still read as a word.
    expect(measurements.withIconFont).toBeLessThan(32);
    expect(measurements.withFallback).toBeGreaterThan(60);
  });

  test('serves the document with security headers', async ({ page }) => {
    // Only the containerised stack puts nginx in front; the dev server serves the same bundle without them, so
    // this is asserted where it is true rather than made true by weakening it.
    test.skip(process.env['E2E_EXPECT_PROD_HEADERS'] !== '1', 'nginx is only in front of the containerised stack');

    const response = await page.goto('/login');
    const headers = response?.headers() ?? {};

    expect(headers['content-security-policy']).toContain("script-src 'self'");
    expect(headers['content-security-policy']).toContain("frame-ancestors 'none'");
    expect(headers['content-security-policy']).not.toContain('fonts.googleapis.com');
    expect(headers['x-content-type-options']).toBe('nosniff');
    expect(headers['x-frame-options']).toBe('DENY');
    expect(headers['referrer-policy']).toBe('no-referrer');
  });
});
