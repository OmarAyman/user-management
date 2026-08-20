import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';

import { LocaleService } from '../services/locale.service';

/**
 * Tells the API which language to answer in.
 *
 * Without this the SPA would be Arabic while its error messages stayed English, which is the sort of split a
 * user notices immediately. One interceptor keeps the two sides in step, so no component has to remember to
 * pass a language.
 */
export const acceptLanguageInterceptor: HttpInterceptorFn = (request, next) => {
  const locale = inject(LocaleService);

  return next(request.clone({ setHeaders: { 'Accept-Language': locale.locale() } }));
};
