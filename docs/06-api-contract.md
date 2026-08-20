# API contract

Base path `/api`. JSON in and out, camelCase, UTF-8. All timestamps are UTC ISO-8601 with offset
(`2026-08-20T09:14:22.117+00:00`). Every endpoint except login and refresh requires
`Authorization: Bearer <accessToken>`.

## 1. Endpoint summary

| Method | Route | Auth | Success | Notes |
|---|---|---|---|---|
| POST | `/api/auth/login` | anonymous | `200` | Sets the refresh cookie |
| POST | `/api/auth/refresh` | refresh cookie | `200` | Rotates the cookie |
| POST | `/api/auth/logout` | authenticated | `204` | Revokes the presented refresh token |
| GET | `/api/users` | any role | `200` | Paged, searchable, sortable, role-filterable. **Active users only — there is no parameter that reveals deleted rows** |
| GET | `/api/users/deleted` | Admin | `200` | The only read path over soft-deleted users (ADR-0004) |
| GET | `/api/users/availability` | authenticated | `200` | Boolean username/email availability for async form validation (ADR-0016) |
| GET | `/api/users/{id}` | any role | `200` | Admin also sees deleted users |
| POST | `/api/users` | Admin | `201` + `Location` | |
| PUT | `/api/users/{id}` | Admin | `200` | Includes role assignment |
| DELETE | `/api/users/{id}` | Admin | `204` | Soft delete |
| POST | `/api/users/{id}/restore` | Admin | `204` | |
| GET | `/api/users/me` | authenticated | `200` | Identity from the token, never from the route |
| PUT | `/api/users/me` | authenticated | `200` | First/last name and email only |
| POST | `/api/users/me/change-password` | authenticated | `204` | Requires the current password |
| GET | `/api/roles` | authenticated | `200` | Read-only |
| GET | `/api/audit-logs` | Admin | `200` | Paged, filterable |

`PUT /api/users/{id}` carries the role rather than a separate `/role` endpoint: role is one field of the
Admin edit form, a dedicated endpoint would force the UI into two calls with no transactional guarantee,
and the audit trail still emits a distinct `RoleChange` row for the delta.

## 2. Authentication

### `POST /api/auth/login`

```json
{ "username": "admin", "password": "<password>" }
```

`200 OK`

```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "expiresAt": "2026-08-20T09:29:22.117+00:00",
  "user": {
    "id": "9f1c...",
    "username": "admin",
    "email": "admin@example.com",
    "firstName": "System",
    "lastName": "Administrator",
    "role": "Admin"
  }
}
```

Also sets `Set-Cookie: refreshToken=<opaque>; HttpOnly; Secure; SameSite=Strict; Path=/api/auth; Max-Age=604800`.
The response body carries no refresh token — a token readable by JavaScript defeats the point of the cookie.

Failures: `400` malformed body; `401 INVALID_CREDENTIALS` for an unknown user, a wrong password (locked or
not), or a soft-deleted account — deliberately indistinguishable; `401 ACCOUNT_LOCKED` with a
`retryAfterSeconds` extension **only** when the password was correct and the account is locked, which
discloses nothing to a caller who did not already know the password (ADR-0006); `429 RATE_LIMITED` when the
auth rate limit trips.

JWT claims: `sub` (user id), `username`, `role`, `jti`, `iat`, `exp`, `iss`, `aud`. Nothing else — no email,
no permissions list, no tenant. Access-token lifetime 15 minutes; refresh-token lifetime 7 days with
rotation.

### `POST /api/auth/refresh`

No body; reads the cookie. `200` returns a new access token and sets a new cookie. `401` if the token is
missing, expired, revoked, or belongs to a now-deleted user. Presenting an already-rotated token revokes
the entire chain for that user and logs at `Warning` (reuse is treated as theft).

### `POST /api/auth/logout`

`204`. Revokes the presented refresh token and clears the cookie. Access tokens are not revocable by
design; the 15-minute lifetime is the bound, and that trade-off is stated in the security plan.

## 3. `GET /api/users`

| Query parameter | Type | Default | Validation |
|---|---|---|---|
| `pageNumber` | int | `1` | `>= 1` |
| `pageSize` | int | `10` | `1..100`; over 100 is a `400`, not a silent clamp |
| `search` | string | none | trimmed; `<= 100` chars; empty is treated as absent |
| `roleId` | int | none | must exist in `Roles` |
| `sortBy` | enum | `createdAt` | whitelist: `username`,`email`,`firstName`,`lastName`,`role`,`createdAt` |
| `sortDirection` | enum | `desc` | `asc` / `desc` |

There is deliberately **no `includeDeleted` parameter**. Soft-delete visibility is an authorization decision,
not a value a client passes, so deleted users are read through `GET /api/users/deleted` behind an Admin
policy. `IgnoreQueryFilters()` is reachable from exactly one repository method with two justified callers —
see ADR-0004.

`200 OK`

```json
{
  "items": [
    {
      "id": "9f1c...",
      "username": "jdoe",
      "email": "jdoe@example.com",
      "firstName": "Jane",
      "lastName": "Doe",
      "role": "User",
      "isDeleted": false,
      "createdAt": "2026-08-01T07:00:00.000+00:00",
      "lastModifiedAt": null
    }
  ],
  "pageNumber": 1,
  "pageSize": 10,
  "totalCount": 27,
  "totalPages": 3,
  "hasPreviousPage": false,
  "hasNextPage": true
}
```

A page beyond the last returns `200` with an empty `items` array — an empty page is not an error, and the
metadata tells the client where it is. `hasPreviousPage`/`hasNextPage` are derived, not stored, and exist so
the UI never recomputes paging arithmetic.

## 4. User CRUD

### `POST /api/users` (Admin)

```json
{
  "username": "asmith",
  "email": "asmith@example.com",
  "firstName": "Alex",
  "lastName": "Smith",
  "password": "<password>",
  "roleId": 2
}
```

`201 Created`, `Location: /api/users/{id}`, body is `UserDetailsDto`.
`400` validation, `409 USERNAME_ALREADY_EXISTS` / `EMAIL_ALREADY_EXISTS`, `403` for non-Admin.

### `PUT /api/users/{id}` (Admin)

```json
{
  "email": "new@example.com",
  "firstName": "Alex",
  "lastName": "Smith",
  "roleId": 1,
  "rowVersion": "AAAAAAAAB9E="
}
```

No `username` (immutable, BR-10), no `password` (separate concern), no `isDeleted` (delete has its own
endpoint), no `createdAt`/`createdBy`. The absent fields are the mass-assignment defence: they cannot be
set because the model cannot carry them.

`rowVersion` is the base64 concurrency token from the `GET` that populated the form, and it is **required**.
EF Core puts it in the `UPDATE` predicate, so a stale value matches zero rows and the write is refused
instead of overwriting someone else's edit (ADR-0013).

`200` with `UserDetailsDto`; `400` when `rowVersion` is missing or malformed; `404` unknown id;
`409 RESOURCE_MODIFIED` when the row changed since it was read; `409` duplicate email or last-Admin
demotion; `422 CANNOT_CHANGE_OWN_ROLE` when an Admin sends a different `roleId` for their own account.

### `DELETE /api/users/{id}` (Admin)

`204`; `403 CANNOT_DELETE_SELF`; `409 USER_ALREADY_DELETED`; `409 LAST_ADMIN_CANNOT_BE_REMOVED`; `404` unknown.

No `rowVersion` required: deletion is idempotent in intent and BR-07 already rejects deleting an
already-deleted row, so a stale delete cannot destroy an unseen edit.

### `POST /api/users/{id}/restore` (Admin)

Body: `{ "rowVersion": "AAAAAAAAB9E=" }`.

`204`; `409 USER_NOT_DELETED`; `409 RESOURCE_MODIFIED`; `409 USERNAME_ALREADY_EXISTS` /
`EMAIL_ALREADY_EXISTS` when the deleted user's identifiers have since been taken by an active user
(BR-17, a direct consequence of ADR-0009); `404` unknown.

### `GET /api/users/deleted` (Admin)

Same paging, search and sort contract as `GET /api/users`; returns only rows where `IsDeleted = 1`, each
carrying `deletedAt` and `deletedBy`. Any non-Admin receives `403 FORBIDDEN` — verified by the authorization
matrix test, since this is the one route where a mistake would expose deleted personal data.

### `GET /api/users/availability` (authenticated)

`?username=asmith&email=asmith@example.com` — either parameter may be omitted.

```json
{ "usernameAvailable": false, "emailAvailable": true }
```

Availability is evaluated against **active** users only, so a soft-deleted user's identifiers report as
available (ADR-0009). This is a UX aid, never an authority: the unique index decides, and a `409` from
`POST`/`PUT` is mapped by the SPA to a field-level error. Rate-limited to keep an async validator from
becoming a scraper.

## 5. Profile

`GET /api/users/me` -> `UserDetailsDto` for the token subject.

`PUT /api/users/me`

```json
{ "firstName": "Jane", "lastName": "Doe", "email": "jane.doe@example.com", "rowVersion": "AAAAAAAAB9E=" }
```

The route has **no id segment**, so there is no identifier for an attacker to swap — IDOR is prevented
structurally rather than by a check that someone might forget. `roleId` and `isDeleted` are not part of the
model, so a payload containing them is ignored (and rejected outright, since the API is configured with
`JsonSerializerOptions.UnmappedMemberHandling = Disallow`, turning a mass-assignment attempt into a `400`).

`POST /api/users/me/change-password`

```json
{ "currentPassword": "<current>", "newPassword": "<new>" }
```

`204`; `400` when the new password fails policy; `401 INVALID_CREDENTIALS` when the current password is
wrong. On success all refresh tokens for the user are revoked and the security stamp rotates.

## 6. Roles and audit

`GET /api/roles` -> `[{ "id": 1, "name": "Admin" }, ...]`

`GET /api/audit-logs` (Admin) — paged like users, plus filters `entityName`, `entityId`, `action`,
`performedByUserId`, `fromUtc`, `toUtc`; default order `timestamp desc`.

```json
{
  "id": 1042,
  "entityName": "User",
  "entityId": "9f1c...",
  "action": "RoleChange",
  "performedByUserId": "1a2b...",
  "performedByUsername": "admin",
  "timestamp": "2026-08-20T09:20:11.004+00:00",
  "ipAddress": "203.0.113.24",
  "oldValues": { "roleId": 2 },
  "newValues": { "roleId": 1 },
  "correlationId": "b7f3..."
}
```

`oldValues`/`newValues` are returned as parsed JSON objects, not escaped strings, so the UI can render a
field-by-field diff without re-parsing.

## 7. Status code policy

| Code | Used for |
|---|---|
| `200` | Successful read or update returning a body |
| `201` | User created; `Location` header set |
| `204` | Delete, restore, logout, change-password — nothing useful to return |
| `400` | Malformed body, failed validation, unknown sort field, page size over the cap, unmapped members, missing/malformed `rowVersion` |
| `401` | Missing, malformed or expired token; failed login; locked account; refresh failure |
| `403` | Authenticated but not permitted (wrong role, self-delete, non-Admin reading `/users/deleted`) |
| `404` | Route or resource does not exist (and is not merely soft-deleted for a non-Admin) |
| `409` | State conflict: duplicate username/email, already deleted, not deleted, last Admin, **concurrent modification** (`RESOURCE_MODIFIED`) |
| `422` | Semantically invalid despite a well-formed payload: changing one's own role |
| `429` | Auth rate limit exceeded |
| `500` | Unexpected failure; body carries only a trace id |

`404` vs `403` for a soft-deleted user viewed by a non-Admin: `404`. A non-Admin has no legitimate way to
learn that a deleted account exists, and `403` would confirm it.

## 8. Error contract

Every error is RFC 7807 `application/problem+json`:

```json
{
  "type": "https://api.usermanagement.local/errors/validation-error",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": "The request contains invalid values.",
  "instance": "POST /api/users",
  "errorCode": "VALIDATION_ERROR",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "errors": {
    "username": ["Username is required.", "Username must be at most 50 characters."],
    "password": ["Password must contain an uppercase letter."]
  }
}
```

- `title` and `detail` are **localized** by `Accept-Language` (`en`, `ar`).
- `errorCode` is **never localized** — it is the stable contract the SPA and the tests branch on. The
  frontend maps the code to its own catalogue so UI copy stays consistent with the rest of the interface.
- `errors` appears only for validation failures.
- `traceId` is the W3C trace parent, and the same value appears in the server logs.

### Error code catalogue

Stable, never localized, declared once in `Domain/Constants/ErrorCodes.cs`, and the key the SPA and the tests
branch on.

| Code | Status | Meaning |
|---|---|---|
| `VALIDATION_ERROR` | 400 | One or more fields failed validation; see `errors` |
| `INVALID_SORT_FIELD` | 400 | `sortBy` outside the whitelist |
| `INVALID_CREDENTIALS` | 401 | Unknown user, wrong password, or soft-deleted account — one response for all three |
| `ACCOUNT_LOCKED` | 401 | Correct password, account temporarily locked; carries `retryAfterSeconds` |
| `UNAUTHENTICATED` | 401 | Missing, malformed or expired token |
| `FORBIDDEN` | 403 | Authenticated, not permitted |
| `CANNOT_DELETE_SELF` | 403 | BR-01 |
| `RESOURCE_NOT_FOUND` | 404 | Target does not exist or is not visible to this caller |
| `RESOURCE_CONFLICT` | 409 | Generic state conflict; used when no specific code applies |
| `RESOURCE_MODIFIED` | 409 | Concurrency token stale — someone else changed the row (ADR-0013) |
| `USERNAME_ALREADY_EXISTS` | 409 | Taken by an **active** user |
| `EMAIL_ALREADY_EXISTS` | 409 | Taken by an active user |
| `USER_ALREADY_DELETED` | 409 | BR-07 |
| `USER_NOT_DELETED` | 409 | BR-08 |
| `LAST_ADMIN_CANNOT_BE_REMOVED` | 409 | BR-02, BR-03 |
| `CANNOT_CHANGE_OWN_ROLE` | 422 | BR-04 |
| `RATE_LIMITED` | 429 | Auth rate limit |
| `INTERNAL_ERROR` | 500 | Unexpected failure; body carries only a trace id |

Specific codes sit alongside the generic ones on purpose: `RESOURCE_CONFLICT` keeps a new conflict from
having to invent a code before anyone knows how the UI should react, while
`LAST_ADMIN_CANNOT_BE_REMOVED` lets the SPA say something genuinely useful. The frontend maps every code
to localized copy in the `errors.*` namespace, and falls back to a generic message for a code it does not
recognise — so adding a code server-side can never produce a blank error dialog.

A `500` body contains `type`, `title` ("An unexpected error occurred."), `status`, `errorCode`
`INTERNAL_ERROR` and `traceId` — no message, no exception type, no stack, no SQL.

## 9. Cross-cutting headers

| Header | Direction | Purpose |
|---|---|---|
| `Authorization: Bearer` | in | access token |
| `Accept-Language` | in | `en` / `ar`; `?culture=` overrides for manual testing |
| `X-Correlation-Id` | in/out | supplied or generated, echoed, logged |
| `Location` | out | on `201` |
| `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Content-Security-Policy` | out | security headers middleware |
| `Server` | out | suppressed |

## 10. Swagger

Swashbuckle with XML comments, a JWT bearer security definition so `Authorize` in the UI works end to end,
`ProducesResponseType` on every action including error shapes, request/response examples for login and
create-user, and the enum whitelists documented as schema enums rather than prose. Enabled in Development;
in Production it is off unless `Swagger:Enabled` is explicitly set.
