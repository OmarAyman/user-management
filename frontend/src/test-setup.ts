/**
 * Test bootstrap, loaded by Angular's unit-test builder before any spec.
 *
 * It exists to make the suite independent of the Node version it happens to run under.
 *
 * The project pins Node 24 (`.nvmrc`), and under Node 24 the browser storage globals in a spec come from
 * jsdom and behave normally. From Node 25 the runtime exposes its own Web Storage `localStorage` and
 * `sessionStorage` on `globalThis`, and because the jsdom environment shares one global object with the host,
 * that built-in shadows jsdom's implementation. Without `--localstorage-file` it is a stub: the property
 * exists, so nothing looks wrong, but `clear`, `getItem` and `setItem` are all `undefined`. Specs that touch
 * storage then fail with "localStorage.clear is not a function", which points at the test rather than at the
 * runtime and cost real time to diagnose.
 *
 * So: use whatever the environment provides when it actually works, and substitute a real in-memory `Storage`
 * only when it does not. On Node 24 this file changes nothing. On Node 25 and later the twelve storage-touching
 * specs pass for the same reason they pass on 24. A reviewer's Node version stops being a variable.
 *
 * The substitute is a genuine `Storage`, not a stub of the calls the specs make. `AuthService` asserts that the
 * access token is never written to storage, and a spy that records nothing would make that assertion pass
 * vacuously - the point is that reads and writes work, and the token still is not there.
 */

export class InMemoryStorage implements Storage {
  private readonly entries = new Map<string, string>();

  get length(): number {
    return this.entries.size;
  }

  clear(): void {
    this.entries.clear();
  }

  getItem(key: string): string | null {
    return this.entries.get(String(key)) ?? null;
  }

  key(index: number): string | null {
    return [...this.entries.keys()][index] ?? null;
  }

  removeItem(key: string): void {
    this.entries.delete(String(key));
  }

  setItem(key: string, value: string): void {
    // Web Storage stringifies both, which is why `setItem('k', 1)` reads back as `'1'`.
    this.entries.set(String(key), String(value));
  }
}

/** Web Storage is only useful here if its methods are actually callable. */
export function isUsable(candidate: unknown): candidate is Storage {
  const storage = candidate as Storage | null | undefined;

  return typeof storage?.clear === 'function'
    && typeof storage.getItem === 'function'
    && typeof storage.setItem === 'function'
    && typeof storage.removeItem === 'function';
}

/**
 * Installs a working `Storage` under <paramref name="name"/> only when the environment's own is unusable.
 *
 * Exported so both branches can be tested on whatever Node version happens to be installed: the
 * leave-it-alone branch is the one that runs on the pinned Node 24, and it would otherwise never be exercised
 * on a machine running something newer.
 */
export function ensureStorage(name: 'localStorage' | 'sessionStorage'): void {
  const host = globalThis as unknown as Record<string, unknown>;

  let existing: unknown;
  try {
    existing = host[name];
  } catch {
    // Node throws on access in some configurations rather than returning a stub.
    existing = undefined;
  }

  if (isUsable(existing)) {
    return;
  }

  Object.defineProperty(globalThis, name, {
    value: new InMemoryStorage(),
    configurable: true,
    writable: true,
  });
}

ensureStorage('localStorage');
ensureStorage('sessionStorage');
