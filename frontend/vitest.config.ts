import { defineConfig } from 'vitest/config';

/**
 * Vitest runner configuration, loaded by Angular's unit-test builder via `--runner-config`.
 *
 * It exists for one reason: reliability under load. Vitest sizes its worker pool from the CPU count, and on a
 * machine already running the Angular dev server or a .NET test host there is not always enough headroom - the
 * run then fails with "Failed to start forks worker" for every file, which looks like a catastrophic test
 * failure and is actually a resource limit. That happened three times during development.
 *
 * The fix is a ceiling on the pool, not a different pool. Switching to `threads` was tried first and is wrong
 * here: `AuthService` and `LocaleService` assert against `localStorage`, and under the threads pool the specs
 * resolve the host's `localStorage` rather than the jsdom window's, so twelve tests failed with
 * "localStorage.clear is not a function". Process isolation is what makes the browser globals per-file, so the
 * pool stays as forks and only its size changes.
 */
export default defineConfig({
  test: {
    poolOptions: {
      forks: {
        // A small ceiling: seven spec files do not need more, and leaving cores free keeps the run predictable
        // when something else is building at the same time.
        maxForks: 4,
        minForks: 1,
      },
    },

    // jsdom setup is the slow part of these runs; the default 5s occasionally clips the first file on a cold
    // start without saying so clearly.
    testTimeout: 15_000,
    hookTimeout: 15_000,
  },
});
