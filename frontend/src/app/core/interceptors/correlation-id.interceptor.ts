import { HttpInterceptorFn } from '@angular/common/http';

/**
 * Tags every request with a correlation id.
 *
 * The server echoes it and writes it into both its logs and its audit rows, so a user reporting "it failed at
 * about ten past three" can be traced to the exact request without guessing from timestamps.
 */
export const correlationIdInterceptor: HttpInterceptorFn = (request, next) =>
  next(request.clone({ setHeaders: { 'X-Correlation-Id': correlationId() } }));

/**
 * A UUID for one request, without assuming a secure context.
 *
 * `crypto.randomUUID` exists **only** in a secure context - HTTPS, or localhost. Served from any other plain
 * HTTP origin it is `undefined`, and calling it throws inside an interceptor that runs on every request. The
 * consequence is not a missing header: the first request out of the application is Transloco fetching its
 * translation catalogue, so the failure surfaces as "Unable to load translation and all the fallback
 * languages" and a blank page. The whole application is dead, and nothing about the message points here.
 *
 * Local development never sees it, because localhost is a secure context by definition. It appeared the first
 * time the built SPA was served from a container hostname over http, which is what any plain-HTTP staging
 * environment looks like.
 *
 * So: use `randomUUID` when it is there, and otherwise build a v4 from `getRandomValues`, which is *not*
 * gated on a secure context. The last resort is `Math.random`, which is not a good source of randomness but is
 * an honest source of a correlation id - this value identifies a request in a log, it is not a secret, and a
 * request with a weak id is strictly better than an application that will not start.
 */
function correlationId(): string {
  const source = globalThis.crypto as Crypto | undefined;

  if (typeof source?.randomUUID === 'function') {
    return source.randomUUID();
  }

  const bytes = new Uint8Array(16);

  if (typeof source?.getRandomValues === 'function') {
    source.getRandomValues(bytes);
  } else {
    for (let index = 0; index < bytes.length; index += 1) {
      bytes[index] = Math.floor(Math.random() * 256);
    }
  }

  // Version 4, variant 1, per RFC 9562 section 5.4.
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;

  const hex = [...bytes].map((byte) => byte.toString(16).padStart(2, '0'));

  return [
    hex.slice(0, 4).join(''),
    hex.slice(4, 6).join(''),
    hex.slice(6, 8).join(''),
    hex.slice(8, 10).join(''),
    hex.slice(10, 16).join(''),
  ].join('-');
}
