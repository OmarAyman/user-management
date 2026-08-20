import { describe, expect, it } from 'vitest';

import arabic from '../../public/i18n/ar.json';
import english from '../../public/i18n/en.json';

/**
 * Keeps the two catalogues in step.
 *
 * A missing Arabic key does not throw - Transloco falls back to English and the page still renders - so the
 * only way this stays correct is a test that compares the key sets.
 */
describe('translation catalogues', () => {
  function flatten(source: unknown, prefix = ''): string[] {
    if (typeof source !== 'object' || source === null) {
      return [prefix];
    }

    return Object.entries(source).flatMap(([key, value]) =>
      flatten(value, prefix === '' ? key : `${prefix}.${key}`),
    );
  }

  const englishKeys = flatten(english).sort();
  const arabicKeys = flatten(arabic).sort();

  it('declare exactly the same keys', () => {
    expect(arabicKeys.filter((key) => !englishKeys.includes(key))).toEqual([]);
    expect(englishKeys.filter((key) => !arabicKeys.includes(key))).toEqual([]);
  });

  it('carry a message for every error code the API can return', () => {
    // Mirrors Domain/Constants/ErrorCodes.cs. A code with no entry would render as a blank dialog, so the
    // frontend keeps its own copy of the list and checks it.
    const codes = [
      'VALIDATION_ERROR',
      'INVALID_SORT_FIELD',
      'INVALID_CREDENTIALS',
      'ACCOUNT_LOCKED',
      'UNAUTHENTICATED',
      'FORBIDDEN',
      'CANNOT_DELETE_SELF',
      'RESOURCE_NOT_FOUND',
      'RESOURCE_CONFLICT',
      'RESOURCE_MODIFIED',
      'USERNAME_ALREADY_EXISTS',
      'EMAIL_ALREADY_EXISTS',
      'USER_ALREADY_DELETED',
      'USER_NOT_DELETED',
      'LAST_ADMIN_CANNOT_BE_REMOVED',
      'CANNOT_CHANGE_OWN_ROLE',
      'RATE_LIMITED',
      'INTERNAL_ERROR',
    ];

    for (const code of codes) {
      expect(englishKeys).toContain(`errors.${code}`);
      expect(arabicKeys).toContain(`errors.${code}`);
    }
  });

  it('define the mandatory unknown-error fallback', () => {
    // So a code added server-side ahead of a frontend deployment degrades instead of showing nothing.
    expect(englishKeys).toContain('errors.unknown');
    expect(arabicKeys).toContain('errors.unknown');
  });

  it('have Arabic values that are actually Arabic', () => {
    const arabicRange = /[؀-ۿ]/;

    const untranslated = Object.entries(flattenValues(arabic))
      .filter(([, value]) => value.length > 0 && !arabicRange.test(value))
      // Language names are deliberately shown in their own script in both catalogues.
      .filter(([key]) => key !== 'common.language.switchToEnglish')
      .map(([key]) => key);

    expect(untranslated).toEqual([]);
  });

  function flattenValues(source: unknown, prefix = ''): Record<string, string> {
    if (typeof source !== 'object' || source === null) {
      return { [prefix]: String(source) };
    }

    return Object.entries(source).reduce<Record<string, string>>(
      (accumulator, [key, value]) =>
        Object.assign(accumulator, flattenValues(value, prefix === '' ? key : `${prefix}.${key}`)),
      {},
    );
  }
});
