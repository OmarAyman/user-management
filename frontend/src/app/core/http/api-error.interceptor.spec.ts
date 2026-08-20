import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';

import { apiErrorInterceptor } from './api-error.interceptor';
import { ApiError } from './api-error.model';

describe('apiErrorInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
  });

  function capture(status: number, body: object | string): Promise<ApiError> {
    return new Promise((resolve) => {
      http.get('/api/users').subscribe({ error: (error: ApiError) => resolve(error) });
      controller.expectOne('/api/users').flush(body, { status, statusText: 'error' });
    });
  }

  it.each([
    [400, 'validation'],
    [422, 'validation'],
    [401, 'unauthenticated'],
    [403, 'forbidden'],
    [404, 'notFound'],
    [409, 'conflict'],
    [429, 'rateLimited'],
    [500, 'server'],
  ])('maps status %i to kind %s', async (status, expectedKind) => {
    const error = await capture(status, { errorCode: 'SOME_CODE', title: 'Title', detail: 'Detail' });

    expect(error.kind).toBe(expectedKind);

    // The stable code survives the mapping: it is what every consumer branches on.
    expect(error.code).toBe('SOME_CODE');
  });

  it('keeps per-field messages for a validation failure', async () => {
    const error = await capture(400, {
      errorCode: 'VALIDATION_ERROR',
      title: 'Invalid',
      errors: { username: ['Username is required.'] },
    });

    expect(error.kind).toBe('validation');
    expect(error.kind === 'validation' && error.fieldErrors['username']).toEqual([
      'Username is required.',
    ]);
  });

  it('keeps the retry hint from a locked account', async () => {
    const error = await capture(401, { errorCode: 'ACCOUNT_LOCKED', retryAfterSeconds: 900 });

    expect(error.kind === 'unauthenticated' && error.retryAfterSeconds).toBe(900);
  });

  it('keeps the trace id from a server failure', async () => {
    const error = await capture(500, { errorCode: 'INTERNAL_ERROR', traceId: 'trace-123' });

    // The only useful thing a 500 body carries, and what a user can quote to support.
    expect(error.kind === 'server' && error.traceId).toBe('trace-123');
  });

  it('distinguishes an unreachable server from a rejected request', async () => {
    const error = await new Promise<ApiError>((resolve) => {
      http.get('/api/users').subscribe({ error: (thrown: ApiError) => resolve(thrown) });
      controller.expectOne('/api/users').error(new ProgressEvent('error'), { status: 0 });
    });

    expect(error.kind).toBe('network');
    expect(error.code).toBe('NETWORK_UNAVAILABLE');
  });

  it('falls back to a generic code when the body carries none', async () => {
    const error = await capture(500, 'not json at all');

    expect(error.code).toBe('INTERNAL_ERROR');
  });
});

