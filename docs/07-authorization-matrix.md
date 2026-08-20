# Authorization matrix

## 1. Capability matrix (the assignment's matrix, with the enforcement point for each row)

| Capability | Admin | User | ReadOnlyUser | Enforced by |
|---|:---:|:---:|:---:|---|
| Login | Yes | Yes | Yes | anonymous endpoint |
| View users | Yes | Yes | Yes | `[Authorize]` |
| Search users | Yes | Yes | Yes | `[Authorize]` |
| Filter users | Yes | Yes | Yes | `[Authorize]` |
| Sort users | Yes | Yes | Yes | `[Authorize]` |
| Create user | Yes | No | No | `[Authorize(Policy = Policies.ManageUsers)]` |
| Edit any user | Yes | No | No | `[Authorize(Policy = Policies.ManageUsers)]` |
| Delete user | Yes | No | No | `Policies.ManageUsers` + BR-01, BR-02 |
| Update own profile | Yes | Yes | Yes | `[Authorize]` on `/users/me` (identity from token) |
| Change own role | No | No | No | field absent from `/users/me`; BR-04 on `/users/{id}` |
| Change another user's role | Yes | No | No | `Policies.ManageUsers` + BR-03 |
| Restore deleted user | Yes | No | No | `Policies.ManageUsers` |
| View deleted users | Yes | No | No | Separate route `GET /api/users/deleted` behind `Policies.ManageUsers` (BR-15) |
| View audit log | Yes | No | No | `[Authorize(Policy = Policies.ViewAuditLogs)]` |

`ReadOnlyUser` can update its own profile: the assignment's matrix says so explicitly, and "read-only"
refers to *other people's* data. This is called out in the README because it is the one row a reviewer is
likely to question.

## 2. Policies

Two policies, defined once in `Policies` constants and registered in `Program.cs`:

```text
Policies.ManageUsers    -> RequireRole(RoleNames.Admin)
Policies.ViewAuditLogs  -> RequireRole(RoleNames.Admin)
```

They are separate despite having identical requirements today, because "who may administer users" and "who
may read the audit trail" are different questions that commonly diverge (an auditor role is the usual next
request). Two names cost nothing now and prevent a rewrite later.

Everything else is plain `[Authorize]` — authenticated, any role. There is no permission table, no claims
transformation and no policy-per-endpoint sprawl: three fixed roles do not justify it.

## 3. Enforcement layers

Defence is layered, and each layer is independently sufficient for its own concern:

| Layer | What it stops | What it does not stop |
|---|---|---|
| Route/model shape (`/users/me` has no id; `UpdateUserRequest` has no role for self; `GET /api/users` has no soft-delete parameter) | IDOR, mass assignment and soft-delete bypass, structurally | nothing — but it only covers what the shape can express |
| ASP.NET Core policies | wrong-role callers, before any handler runs | same-role business violations (self-delete, last Admin) |
| Handler business rules (BR-01..BR-15) | semantic violations by an otherwise authorized caller | nothing below the transaction |
| Database constraints | duplicate username/email under a race | role logic |
| Angular guards + `*appHasRole` | showing actions a user cannot perform | nothing (UX only) |

## 4. IDOR: the concrete attack and why it fails

The assignment names it directly: a `User` calls `PUT /api/users/{anotherUserId}`.

1. `PUT /api/users/{id}` carries `Policies.ManageUsers`. A `User` token is rejected with `403` before the
   action body executes and before the id is even parsed.
2. The only self-service write path is `PUT /api/users/me`, whose handler resolves the target from
   `ICurrentUserService.UserId` — read from the validated JWT `sub` claim. There is no code path where a
   client-supplied identifier selects the row being updated.
3. An Admin *can* update any user by id — that is the intended capability, not an IDOR.

Integration tests cover all three: `User` -> `403`, `ReadOnlyUser` -> `403`, Admin -> `200`; and a `User`
sending someone else's id inside a `PUT /users/me` body still updates only their own row.

## 5. Mass assignment: the concrete attack and why it fails

The assignment names the payload: `{ "role": "Admin", "isDeleted": false }` sent to a profile update.

1. `UpdateProfileRequest` has exactly three properties: `firstName`, `lastName`, `email`. There is no
   `role`, `roleId`, `isDeleted`, `passwordHash`, `createdAt` or `id` to bind.
2. `JsonSerializerOptions.UnmappedMemberHandling = Disallow` means unknown members are not silently
   dropped — the request is rejected with `400`. Silent dropping is safe but invisible; rejecting makes the
   attempt loud in logs and honest to the caller.
3. EF entities are never model-bound. Controllers accept request records, map to commands, and handlers
   mutate entities through domain methods.
4. Role assignment exists only on the Admin path, and even there BR-04 blocks self-elevation.

## 6. Soft-delete visibility is authorization, not a parameter

Soft-deleted rows contain personal data of people who were removed from the system, so who may read them is
an authorization question. The design keeps it one:

| Rule | Mechanism |
|---|---|
| Deleted rows are invisible by default | EF Core global query filter on `User` |
| `IgnoreQueryFilters()` exists in exactly one place | `UserRepository.QueryIncludingDeleted()`; an architecture test fails the build if the call appears anywhere else |
| Only two callers may use it | `GetDeletedUsersQueryHandler` (Admin policy) and `LoginCommandHandler` (must see a deleted row to refuse it, BR-05) |
| No client input selects it | `GET /api/users` has no `includeDeleted` parameter; deleted users live behind `GET /api/users/deleted` with its own `[Authorize(Policy = Policies.ManageUsers)]` |
| A non-Admin cannot reach it at all | Route-level policy, verified for both `User` and `ReadOnlyUser` in the authorization matrix theory |

The earlier design accepted `?includeDeleted=true` and checked the caller's role inside the handler. It
worked, but it made a data-exposure decision depend on a conditional that a future refactor could drop.
Moving it to the route turns "did anyone remember the check?" into "does the route exist?", which the
framework answers. Recorded as an amendment to ADR-0004.

## 7. Role escalation paths, closed

| Path an attacker might try | Outcome |
|---|---|
| Send `roleId` to `PUT /users/me` | `400` — unmapped member |
| Edit own record via `PUT /users/{ownId}` as non-Admin | `403` — policy |
| Admin edits own record to a different role | `422 CANNOT_CHANGE_OWN_ROLE` |
| Forge `role` claim in a JWT | signature validation fails -> `401` |
| Reuse an old token after being demoted | role change rotates the security stamp and revokes refresh tokens; the current access token expires within 15 minutes. Documented residual window in the security plan |
| Delete every Admin to force an escalation | `409 LAST_ADMIN_CANNOT_BE_REMOVED` |

## 8. Frontend authorization (UX only)

- `authGuard` — redirects unauthenticated users to `/login` with a `returnUrl`.
- `roleGuard(roles)` — route data declares required roles; failure routes to `/forbidden`, it does not
  silently redirect home (silent redirects make bugs look like features).
- `*appHasRole="'Admin'"` — hides create/edit/delete/restore controls and the Audit nav item.
- The 401 interceptor attempts one refresh, then clears the session and routes to `/login`. A `403`
  surfaces a localized toast and leaves the page intact — the user is authenticated, just not permitted.

None of this is security. The test suite asserts the server returns `403` for the same operations the UI
hides, which is the only claim that matters.
