import { HttpInterceptorFn } from '@angular/common/http';

/**
 * Tags every request with a correlation id.
 *
 * The server echoes it and writes it into both its logs and its audit rows, so a user reporting "it failed at
 * about ten past three" can be traced to the exact request without guessing from timestamps.
 */
export const correlationIdInterceptor: HttpInterceptorFn = (request, next) =>
  next(request.clone({ setHeaders: { 'X-Correlation-Id': crypto.randomUUID() } }));
