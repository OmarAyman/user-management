import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';

export type AppLocale = 'en' | 'ar';

const STORAGE_KEY = 'ui.locale';

/**
 * The active language and text direction.
 *
 * Direction is set once on `<html>`; Angular Material and the CDK read it from there through `Directionality`,
 * so overlays, menus, sort arrows and the paginator all flip without any component knowing about it. That is
 * why there is no `isRtl` flag threaded through the UI.
 */
@Injectable({ providedIn: 'root' })
export class LocaleService {
  private readonly transloco = inject(TranslocoService);
  private readonly document = inject(DOCUMENT);

  private readonly localeSignal = signal<AppLocale>('en');

  readonly locale = this.localeSignal.asReadonly();
  readonly direction = computed<'ltr' | 'rtl'>(() => (this.localeSignal() === 'ar' ? 'rtl' : 'ltr'));
  readonly isRtl = computed(() => this.direction() === 'rtl');

  /** Resolution order: a stored preference, then the browser's language, then English. */
  initialise(): void {
    const stored = this.readStored();
    const browser = this.document.defaultView?.navigator.language ?? '';
    const initial: AppLocale = stored ?? (browser.startsWith('ar') ? 'ar' : 'en');

    this.apply(initial);
  }

  setLocale(locale: AppLocale): void {
    this.apply(locale);

    // A language preference is not a credential, so localStorage is the right place for it - the rule against
    // web storage applies to tokens.
    try {
      this.document.defaultView?.localStorage.setItem(STORAGE_KEY, locale);
    } catch {
      // Storage can be unavailable in private modes. Losing a preference is not worth failing a click over.
    }
  }

  toggle(): void {
    this.setLocale(this.localeSignal() === 'en' ? 'ar' : 'en');
  }

  private apply(locale: AppLocale): void {
    this.localeSignal.set(locale);
    this.transloco.setActiveLang(locale);

    const root = this.document.documentElement;
    root.lang = locale;
    root.dir = locale === 'ar' ? 'rtl' : 'ltr';
  }

  private readStored(): AppLocale | null {
    try {
      const value = this.document.defaultView?.localStorage.getItem(STORAGE_KEY);

      return value === 'en' || value === 'ar' ? value : null;
    } catch {
      return null;
    }
  }
}
