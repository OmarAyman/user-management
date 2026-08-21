import { defineConfig, devices } from '@playwright/test';

/**
 * Records the walkthrough in docs/14-demo-script.md as a video.
 *
 * Separate from playwright.config.ts on purpose: this is not a test. It asserts almost nothing, it runs for
 * minutes because a viewer needs time to read, and it must never be part of `npm run e2e` - a suite that takes
 * five minutes to prove nothing is a suite people stop running.
 *
 * Two limits of the format, both worked around in the spec rather than hidden: Playwright records the page and
 * not the browser chrome, so an address bar cannot be shown, and it cannot open DevTools. Anything that would
 * normally be demonstrated there - the URL, local storage, a raw API response - is drawn as an on-page overlay
 * instead, which is honest as long as the overlay shows real values read from the running page.
 */
export default defineConfig({
  testDir: './demo',
  fullyParallel: false,
  workers: 1,
  retries: 0,

  // Long: the recording is paced for a human watching, not for a machine asserting.
  timeout: 15 * 60 * 1000,
  expect: { timeout: 15_000 },

  reporter: [['list']],

  use: {
    baseURL: process.env['DEMO_BASE_URL'] ?? 'http://localhost:4200',

    // 720p, and the video matches the viewport exactly so nothing is scaled or letterboxed.
    viewport: { width: 1280, height: 720 },
    video: { mode: 'on', size: { width: 1280, height: 720 } },

    // Every action visibly deliberate. Without this the interface reacts faster than a viewer can follow, and
    // the recording reads as a glitch reel rather than a demonstration.
    launchOptions: { slowMo: 250 },
  },

  outputDir: './demo-recording',

  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], channel: undefined } }],
});
