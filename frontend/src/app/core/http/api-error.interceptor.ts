import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

import { ApiError, ErrorCode } from './api-error.model';

/**
 * The only place in the application that reads an HTTP status code.
 *
 * Everything downstream - the refresh interceptor, the components, the toasts - branches on the typed `kind`
 * and the server's stable `errorCode`. Concentrating the translation here is what stops "was that a 409 or a
 * 422?" from being re-decided in every feature.
 */
export const apiErrorInterceptor: HttpInterceptorFn = (request, next) =>
  next(request).pipe(catchError((error: HttpErrorResponse) => throwError(() => toApiError(error))));

function toApiError(error: HttpErrorResponse): ApiError {
  // Status 0 means the request never reached the server: a dead API and a rejected request are different
  // problems and deserve different messages.
  if (error.status === 0) {
    return {
      kind: 'network',
      code: ErrorCode.networkUnavailable,
      message: 'The server could not be reached.',
    };
  }

  const problem = (error.error ?? {}) as {
    errorCode?: string;
    title?: string;
    detail?: string;
    traceId?: string;
    retryAfterSeconds?: number;
    errors?: Record<string, string[]>;
  };

  // The server's localized sentence is preferred, because it is already in the caller's language. The SPA
  // falls back to its own catalogue only when the response carries nothing usable.
  const code = problem.errorCode ?? ErrorCode.internalError;
  const message = problem.detail ?? problem.title ?? error.message;

  switch (error.status) {
    case 400:
    case 422:
      return {
        kind: 'validation',
        code,
        message,
        fieldErrors: problem.errors ?? {},
      };

    case 401:
      return { kind: 'unauthenticated', code, message, retryAfterSeconds: problem.retryAfterSeconds };

    case 403:
      return { kind: 'forbidden', code, message };

    case 404:
      return { kind: 'notFound', code, message };

    case 409:
      return { kind: 'conflict', code, message };

    case 429:
      return { kind: 'rateLimited', code, message, retryAfterSeconds: problem.retryAfterSeconds };

    default:
      // A 500 body carries only a code and a trace id by design, so the trace id is the one useful thing to
      // keep - it is what a user can quote to support.
      return { kind: 'server', code, message, traceId: problem.traceId };
  }
}
