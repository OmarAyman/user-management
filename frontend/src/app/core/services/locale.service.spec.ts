import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTransloco } from '@jsverse/transloco';
import { Observable, of } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';

import { LocaleService } from './locale.service';

class StubLoader {
  getTranslation(): Observable<Record<string, string>> {
    return of({});
  }
}

describe('LocaleService', () => {
  let service: LocaleService;

  beforeEach(() => {
    localStorage.clear();
    document.documentElement.dir = 'ltr';
    document.documentElement.lang = 'en';

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTransloco({
          config: { availableLangs: ['en', 'ar'], defaultLang: 'en' },
          loader: StubLoader,
        }),
      ],
    });

    service = TestBed.inject(LocaleService);
  });

  it('defaults to English left-to-right', () => {
    service.initialise();

    expect(service.locale()).toBe('en');
    expect(service.direction()).toBe('ltr');
    expect(document.documentElement.dir).toBe('ltr');
  });

  /**
   * Direction is set once on the document element; Material and the CDK read it from there, which is why no
   * component in the application carries an RTL flag.
   */
  it('flips the document to rtl for Arabic and back again', () => {
    service.setLocale('ar');

    expect(service.isRtl()).toBe(true);
    expect(document.documentElement.dir).toBe('rtl');
    expect(document.documentElement.lang).toBe('ar');

    service.setLocale('en');

    expect(service.isRtl()).toBe(false);
    expect(document.documentElement.dir).toBe('ltr');
    expect(document.documentElement.lang).toBe('en');
  });

  it('toggles between the two languages', () => {
    service.initialise();
    service.toggle();

    expect(service.locale()).toBe('ar');

    service.toggle();

    expect(service.locale()).toBe('en');
  });

  it('remembers the choice and restores it', () => {
    service.setLocale('ar');

    // A language preference is not a credential, so storing it is fine - the rule against web storage applies
    // to tokens.
    expect(localStorage.getItem('ui.locale')).toBe('ar');

    const restored = TestBed.inject(LocaleService);
    restored.initialise();

    expect(restored.locale()).toBe('ar');
  });
});
