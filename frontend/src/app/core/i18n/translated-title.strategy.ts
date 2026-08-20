import { DestroyRef, inject, Injectable } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { BehaviorSubject, filter, switchMap } from 'rxjs';

/**
 * Sets the browser tab title from a translation key on the route.
 *
 * Angular's default strategy writes `route.title` to the document verbatim, so a route declaring
 * `title: 'Users'` shows English in an Arabic session - the one piece of user-visible text that escaped the
 * localization rule, because it lives outside every template.
 *
 * The subscription is what makes it correct, and the shape of it is not incidental:
 *
 * - `selectTranslate` re-emits on every language change, so switching language updates the tab without a
 *   navigation. `langChanges$` plus a synchronous `translate()` is the obvious version and it is wrong: the new
 *   catalogue is still loading when the language changes, `translate()` answers with the key it was given, and
 *   the tab reads "users.list.title" until the next navigation. A browser test caught exactly that.
 * - `switchMap` over the current key drops the previous route's translation stream, so a fast navigation cannot
 *   have a late emission overwrite the newer title.
 * - A key that resolves to nothing falls back to the application name rather than showing a raw dotted path.
 */
@Injectable()
export class TranslatedTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly translations = inject(TranslocoService);

  /** The key for the route currently shown; `undefined` before the first navigation that declares one. */
  private readonly currentKey = new BehaviorSubject<string | undefined>(undefined);

  constructor() {
    super();

    this.currentKey
      .pipe(
        filter((key): key is string => key !== undefined),
        switchMap((key) => this.translations.selectTranslate<string>(key)),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((translated) => {
        this.title.setTitle(this.presentable(translated));
      });
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const key = this.buildTitle(snapshot);

    if (key === undefined) {
      // A route without a title leaves the previous one in place, which is Angular's own behaviour.
      return;
    }

    this.currentKey.next(key);
  }

  private presentable(translated: string | undefined): string {
    const key = this.currentKey.value;

    // Transloco echoes the key when it cannot resolve it. A tab reading "users.list.pageTitle" is worse than
    // one reading the application name.
    if (translated !== undefined && translated !== '' && translated !== key) {
      return translated;
    }

    const applicationName = this.translations.translate<string>('app.title');

    return applicationName === 'app.title' ? (key ?? '') : applicationName;
  }
}
