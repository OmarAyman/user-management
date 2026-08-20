import { defineConfig, devices } from '@playwright/test';

/**
 * A smoke suite, deliberately small.
 *
 * Five specs that prove the SPA and the API actually meet in a browser - the one thing neither component tests
 * (mocked HTTP) nor API tests (no browser) can show. Deep behaviour stays at those cheaper layers, where it
 * does not flake (ADR-0015).
 *
 * Guardrails: one browser, no visual snapshots, no page-object framework beyond a login helper, and test data
 * created through the API rather than clicked into existence.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,

  // No retries. A smoke test that only passes on the second attempt is telling you something, and retrying it
  // into green is how that signal gets lost.
  retries: 0,
  workers: 1,

  reporter: [['list']],
  timeout: 30_000,
  expect: { timeout: 10_000 },

  use: {
    baseURL: process.env['E2E_BASE_URL'] ?? 'http://localhost:4200',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'], channel: undefined },
    },
  ],
});
