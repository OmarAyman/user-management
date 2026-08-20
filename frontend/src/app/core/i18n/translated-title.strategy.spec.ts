import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { provideRouter, TitleStrategy } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTransloco, TranslocoService } from '@jsverse/transloco';
import { delay, firstValueFrom, Observable, of } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';

import { TranslatedTitleStrategy } from './translated-title.strategy';
import arabic from '../../../../public/i18n/ar.json';
import english from '../../../../public/i18n/en.json';

/**
 * The loader is deliberately slow.
 *
 * An instant loader hides the defect this strategy exists to avoid: a real catalogue arrives over HTTP, so when
 * the language changes the new translations are not there yet. The first version of this spec preloaded both
 * catalogues, passed, and a browser test then found the tab showing the raw key `users.list.title`. A delay of
 * one tick is enough to model the ordering that matters.
 */
class SlowLoader {
  getTranslation(lang: string): Observable<Record<string, unknown>> {
    return of(lang === 'ar' ? arabic : english).pipe(delay(1));
  }
}

@Component({ template: '' })
class BlankPage {}

/**
 * Driven through the real router rather than a hand-built snapshot: Angular resolves a route's title into the
 * snapshot under a private symbol and `buildTitle` walks the child chain to find it, so a stub snapshot would
 * test the stub. Navigating for real also covers the wiring - that this is the strategy the router calls.
 */
describe('TranslatedTitleStrategy', () => {
  let harness: RouterTestingHarness;
  let title: Title;
  let translations: TranslocoService;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'users', component: BlankPage, title: 'users.list.title' },
          { path: 'plain', component: BlankPage },
          { path: 'missing', component: BlankPage, title: 'nothing.here.at.all' },
        ]),
        provideTransloco({
          config: { availableLangs: ['en', 'ar'], defaultLang: 'en', reRenderOnLangChange: true },
          loader: SlowLoader,
        }),
        { provide: TitleStrategy, useClass: TranslatedTitleStrategy },
      ],
    });

    translations = TestBed.inject(TranslocoService);
    title = TestBed.inject(Title);

    // Only English is preloaded. Arabic is not, on purpose: the language-switch test needs the catalogue to
    // arrive after the switch, which is what happens in a browser.
    await firstValueFrom(translations.load('en'));
    translations.setActiveLang('en');

    harness = await RouterTestingHarness.create();
  });

  it('translates a route title key', async () => {
    await harness.navigateByUrl('/users');

    await expect.poll(() => title.getTitle()).toBe(english.users.list.title);
  });

  it('re-translates the current title when the language changes', async () => {
    await harness.navigateByUrl('/users');
    await expect.poll(() => title.getTitle()).toBe(english.users.list.title);

    translations.setActiveLang('ar');

    // The router does not navigate again on a language switch, so without the subscription the tab would keep
    // the English title for the rest of the session.
    await expect.poll(() => title.getTitle()).toBe(arabic.users.list.title);
  });

  it('never shows the raw key while a new catalogue is loading', async () => {
    await harness.navigateByUrl('/users');
    await expect.poll(() => title.getTitle()).toBe(english.users.list.title);

    translations.setActiveLang('ar');

    // The regression this pins: translating synchronously on the language-change event answers with the key,
    // because the Arabic catalogue has not arrived. The previous title is the right thing to show meanwhile.
    expect(title.getTitle()).toBe(english.users.list.title);
  });

  it('falls back to the application name rather than showing a raw key', async () => {
    await harness.navigateByUrl('/missing');

    await expect.poll(() => title.getTitle()).toBe(english.app.title);
  });

  it('leaves the title alone for a route that declares none', async () => {
    title.setTitle('untouched');

    await harness.navigateByUrl('/plain');

    expect(title.getTitle()).toBe('untouched');
  });

  // The companion check - that every real route carries a key both catalogues define - lives in
  // src/app/route-titles.spec.ts. It has to read the feature route tables, and `core` is not allowed to see a
  // feature: the boundary lint rule refused this file's first draft for importing `features/users`, which is
  // the rule doing precisely what it exists to do.
});
