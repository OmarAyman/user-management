import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Translation, TranslocoLoader } from '@jsverse/transloco';
import { Observable } from 'rxjs';

/**
 * Loads a translation catalogue at runtime.
 *
 * Runtime loading is the reason for choosing a library over Angular's built-in `$localize`, which compiles one
 * bundle per locale and would need a different URL and a reload to switch language - failing the requirement
 * that the UI switch interactively (ADR-0007).
 */
@Injectable({ providedIn: 'root' })
export class TranslocoHttpLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);

  getTranslation(lang: string): Observable<Translation> {
    return this.http.get<Translation>(`/i18n/${lang}.json`);
  }
}
