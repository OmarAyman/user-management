/**
 * The typed error the rest of the application sees.
 *
 * `HttpErrorResponse` never leaves `core/http`. Everything downstream branches on `kind` and `code`, which is
 * what keeps status-code knowledge in one file instead of spread across every component that makes a call.
 */
export type ApiError =
  | { readonly kind: 'validation'; readonly code: string; readonly message: string; readonly fieldErrors: Readonly<Record<string, readonly string[]>> }
  | { readonly kind: 'unauthenticated'; readonly code: string; readonly message: string; readonly retryAfterSeconds?: number }
  | { readonly kind: 'forbidden'; readonly code: string; readonly message: string }
  | { readonly kind: 'notFound'; readonly code: string; readonly message: string }
  | { readonly kind: 'conflict'; readonly code: string; readonly message: string }
  | { readonly kind: 'rateLimited'; readonly code: string; readonly message: string; readonly retryAfterSeconds?: number }
  | { readonly kind: 'server'; readonly code: string; readonly message: string; readonly traceId?: string }
  | { readonly kind: 'network'; readonly code: string; readonly message: string };

/**
 * Error codes the UI reacts to specifically. The rest are rendered from their own message; this list is only
 * the ones that change behaviour rather than wording.
 */
export const ErrorCode = {
  validationError: 'VALIDATION_ERROR',
  invalidCredentials: 'INVALID_CREDENTIALS',
  accountLocked: 'ACCOUNT_LOCKED',
  unauthenticated: 'UNAUTHENTICATED',
  forbidden: 'FORBIDDEN',
  resourceModified: 'RESOURCE_MODIFIED',
  usernameAlreadyExists: 'USERNAME_ALREADY_EXISTS',
  emailAlreadyExists: 'EMAIL_ALREADY_EXISTS',
  lastAdminCannotBeRemoved: 'LAST_ADMIN_CANNOT_BE_REMOVED',
  cannotChangeOwnRole: 'CANNOT_CHANGE_OWN_ROLE',
  cannotDeleteSelf: 'CANNOT_DELETE_SELF',
  userAlreadyDeleted: 'USER_ALREADY_DELETED',
  userNotDeleted: 'USER_NOT_DELETED',
  rateLimited: 'RATE_LIMITED',
  networkUnavailable: 'NETWORK_UNAVAILABLE',
  internalError: 'INTERNAL_ERROR',
} as const;

/** Maps a server error code to the field it belongs to, so a conflict lands on the input that caused it. */
export const CONFLICT_FIELD: Readonly<Record<string, string>> = {
  [ErrorCode.usernameAlreadyExists]: 'username',
  [ErrorCode.emailAlreadyExists]: 'email',
};

export function isApiError(value: unknown): value is ApiError {
  return typeof value === 'object' && value !== null && 'kind' in value && 'code' in value;
}
