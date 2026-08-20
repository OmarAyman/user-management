import { describe, expect, it } from 'vitest';

import { routes } from './app.routes';
import { userRoutes } from './features/users/users.routes';
import arabic from '../../public/i18n/ar.json';
import english from '../../public/i18n/en.json';

/**
 * Route titles are the one piece of user-visible text that lives outside a template, which is how they stayed
 * English while everything else was translated.
 *
 * `TranslatedTitleStrategy` resolves whatever the route declares; it cannot tell a translation key from a
 * sentence, because literal English text resolves to itself and looks entirely correct in an English session.
 * This is the check that catches that: every declared title must be a key that both catalogues define.
 *
 * It sits at app root rather than beside the strategy because it reads the feature route tables, and `core` may
 * not import a feature.
 */
describe('route titles', () => {
  const titles = [...collectTitles(routes), ...collectTitles(userRoutes)];

  it('are declared on the routes that need them', () => {
    // Seven: sign-in, users list, create, edit, profile, audit, forbidden. A drop here means a screen lost its
    // tab title rather than that the check got easier.
    expect(titles).toHaveLength(7);
  });

  it('are translation keys, not literal text', () => {
    for (const title of titles) {
      expect(typeof title, `route title ${String(title)} must be a string key`).toBe('string');

      // A sentence gives away a literal: keys here are dotted paths with no spaces.
      expect(title as string).toMatch(/^[a-z][A-Za-z]*(\.[A-Za-z]+)+$/);
    }
  });

  it('resolve in both catalogues', () => {
    for (const title of titles) {
      expect(resolve(english, title as string), `${title as string} missing from en.json`).toBeTypeOf('string');
      expect(resolve(arabic, title as string), `${title as string} missing from ar.json`).toBeTypeOf('string');
    }
  });

  it('reuse the keys the pages themselves display', () => {
    // A separate key per tab title would drift from the heading on the page. These are the page headings.
    expect(titles).toContain('users.list.title');
    expect(titles).toContain('users.form.createTitle');
    expect(titles).toContain('users.form.editTitle');
    expect(titles).toContain('profile.title');
    expect(titles).toContain('audit.title');
    expect(titles).toContain('auth.login.title');
    expect(titles).toContain('forbidden.title');
  });
});

function collectTitles(routes: readonly { title?: unknown; children?: readonly unknown[] }[]): unknown[] {
  return routes.flatMap((route) => [
    ...(route.title === undefined ? [] : [route.title]),
    ...collectTitles((route.children ?? []) as { title?: unknown; children?: readonly unknown[] }[]),
  ]);
}

function resolve(catalogue: unknown, key: string): unknown {
  return key
    .split('.')
    .reduce<unknown>(
      (node, segment) =>
        typeof node === 'object' && node !== null ? (node as Record<string, unknown>)[segment] : undefined,
      catalogue,
    );
}
