import { afterEach, describe, expect, it } from 'vitest';

import { ensureStorage, InMemoryStorage, isUsable } from './test-setup';

/**
 * Guards the test bootstrap in `test-setup.ts`.
 *
 * `AuthService` and `LocaleService` both assert against browser storage, and when storage is silently broken
 * they fail with "localStorage.clear is not a function" - twelve failures that point at the specs rather than
 * at the runtime shadowing jsdom's implementation. These four assertions fail first and say what is actually
 * wrong, so the same half-hour is not spent diagnosing it twice.
 */
describe('test environment', () => {
  it('provides working local storage', () => {
    expect(typeof localStorage.setItem).toBe('function');
    expect(typeof localStorage.getItem).toBe('function');
    expect(typeof localStorage.removeItem).toBe('function');
    expect(typeof localStorage.clear).toBe('function');
  });

  it('round-trips a value through local storage', () => {
    localStorage.clear();
    localStorage.setItem('probe', 'value');

    expect(localStorage.getItem('probe')).toBe('value');
    expect(localStorage.length).toBe(1);

    localStorage.removeItem('probe');
    expect(localStorage.getItem('probe')).toBeNull();
  });

  it('clears local storage completely', () => {
    localStorage.setItem('a', '1');
    localStorage.setItem('b', '2');

    localStorage.clear();

    expect(localStorage.length).toBe(0);
    expect(localStorage.getItem('a')).toBeNull();
  });

  it('provides working session storage', () => {
    // AuthService asserts the access token reaches neither store, so both have to be real for that to mean
    // anything.
    sessionStorage.clear();
    sessionStorage.setItem('probe', 'value');

    expect(sessionStorage.getItem('probe')).toBe('value');

    sessionStorage.clear();
    expect(sessionStorage.length).toBe(0);
  });
});

/**
 * The installer's two branches.
 *
 * Only one of them runs on any given machine - the leave-it-alone branch on the pinned Node 24, the substitute
 * branch on Node 25 and later. Testing them directly means the branch this machine does not take is still
 * covered, which is the whole point of pinning behaviour rather than a version.
 */
describe('ensureStorage', () => {
  const original = Object.getOwnPropertyDescriptor(globalThis, 'sessionStorage');

  afterEach(() => {
    if (original === undefined) {
      delete (globalThis as unknown as Record<string, unknown>)['sessionStorage'];
    } else {
      Object.defineProperty(globalThis, 'sessionStorage', original);
    }
  });

  function install(value: unknown): void {
    Object.defineProperty(globalThis, 'sessionStorage', { value, configurable: true, writable: true });
  }

  it('leaves a working storage untouched', () => {
    const working = new InMemoryStorage();
    working.setItem('kept', 'yes');
    install(working);

    ensureStorage('sessionStorage');

    // The same object, not a replacement: on Node 24 the specs must keep using jsdom's own implementation.
    expect(globalThis.sessionStorage).toBe(working);
    expect(globalThis.sessionStorage.getItem('kept')).toBe('yes');
  });

  it('substitutes a working storage when the runtime provides a stub', () => {
    // What Node 25 exposes without --localstorage-file: the property is there, the methods are not.
    install({ length: 0 });

    ensureStorage('sessionStorage');

    expect(isUsable(globalThis.sessionStorage)).toBe(true);
    globalThis.sessionStorage.setItem('k', 'v');
    expect(globalThis.sessionStorage.getItem('k')).toBe('v');
  });

  it('substitutes a working storage when access throws', () => {
    Object.defineProperty(globalThis, 'sessionStorage', {
      configurable: true,
      get() {
        throw new Error('storage unavailable');
      },
    });

    expect(() => ensureStorage('sessionStorage')).not.toThrow();
    expect(isUsable(globalThis.sessionStorage)).toBe(true);
  });

  it('treats a partially implemented storage as unusable', () => {
    // A half-shim is worse than none: it would let a spec write and never read back.
    expect(isUsable({ clear: () => undefined })).toBe(false);
    expect(isUsable(new InMemoryStorage())).toBe(true);
    expect(isUsable(undefined)).toBe(false);
  });
});
