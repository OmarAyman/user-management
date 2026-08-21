# Five-minute demo script

A recording plan. Every claim below is something the running application actually does — no slides, no
narration over a static screen.

**Before you record**, bring the application up. One command is enough:

```bash
docker compose up --build      # everything on http://localhost:4200
```

Or run it locally if you would rather show the dev server: `dotnet run --project src/UserManagement.Api --urls
http://localhost:5080` and `npm start` in `frontend/`.

Either way, have Swagger open in a second tab (`http://localhost:5080/swagger`) and sign out first, so the
recording starts at the sign-in screen. If you have restarted the stack a few times while rehearsing, run
`docker compose --profile test down -v` and `up` again for a clean 28-user list.

**A recorded visual track exists.** `npm run demo:record --prefix frontend` drives the application through
every beat below and writes a 720p video to `frontend/demo-recording/`. It is silent by design: the value of
this demo is what gets said over each screen, and no script can supply that. Use it to rehearse, to narrate
over, or as a fallback if a live recording will not cooperate.

Two things it does differently from a person at a keyboard, because Playwright records the page rather than the
browser window: it cannot show the address bar and it cannot open DevTools. Where the script says "the URL
updates" or "open local storage", the recording reads the real value from the live page and draws it as an
overlay. Nothing on screen is authored text pretending to be output.

**One deliberate deviation from the obvious running order:** soft delete comes *before* the audit trail. The
reverse order is more natural to describe, but it means opening the audit screen before the deletion has
happened — so the most interesting rows are missing from the very screen meant to show them. Deleting first
means the audit segment displays a complete lifecycle (created, updated, role changed, deleted, restored) that
the viewer just watched happen, which is the whole point of that minute.

---

## 0:00–0:30 · Sign in

1. Land on `/login`. Point out the form: labels, a password reveal, validation on submit.
2. Enter a wrong password once. The failure appears **inline and in a live region**, not as a toast that can be
   missed — and it says only "the username or password is incorrect".
3. Say the one sentence that matters: *an unknown username, a wrong password and a deleted account all give
   that same answer, so sign-in cannot be used to find out whether an account exists.*
4. Sign in as `admin` / `Admin@123456`.

## 0:30–1:30 · Admin user management

1. The list loads: 28 users, newest first, with role and status per row.
2. **Create a user.** Click *Create user*, and while typing show:
   - the username hint (immutable after creation),
   - the password policy hint,
   - the availability check firing **on blur** — type `admin` into username, tab away, and the field reports
     it as taken before submitting.
3. Save. The new user appears in the list.
4. **Edit** it: change the role from *User* to *Read-only user* and save. Mention that the role lives on the
   admin route only.
5. Open **DevTools → Application → Local storage** for five seconds. It contains exactly one key,
   `ui.locale`. Say it: *the access token is held in memory and never written to storage, so an injected
   script cannot read it out later.*

## 1:30–2:15 · Search, filter, sort, paging

1. Type `khalil` in search — one request, not one per keystroke (debounced), and the **URL updates**.
2. Filter by role — the URL updates again.
3. Click the *Username* column twice to sort ascending then descending.
4. Change the page size and step to page 2.
5. Copy the URL, open it in a new tab. *The list state lives in the URL, so a filtered view is shareable, the
   back button works, and a reload lands where you were.*
6. Search for something impossible (`zzzzz`) to show the **empty state** — not a blank table.

## 2:15–2:45 · Role-based authorization

1. Sign out. Sign in as `readonly` / `ReadOnly@1234`.
2. The list is there; *Create user*, edit, delete, the deleted-users toggle and the **Audit** nav item are all
   gone.
3. Then make the point that matters. In DevTools console:
   ```js
   await fetch('/api/audit-logs', { credentials: 'include' }).then(r => r.status)   // 401 / 403
   ```
   *Hiding a button is courtesy. The server refuses the same operations, and the test suite proves it for all
   three roles across every mutating endpoint.*

## 2:45–3:15 · User profile and password change

1. Still as `readonly`, open **Profile**. Update the first name and save.
2. Point at what is **not** there: no role control, and the note explaining that an administrator sets it.
   *The profile model has no role field at all, so self-elevation is not something the UI declines — it is
   something the contract cannot express.*
3. Change the password on the same page: enter the current one, then a new one. The confirmation says other
   sessions have been signed out — say that out loud, because it is the security behaviour, not a nicety:
   *a password change revokes every refresh-token family for that user.*

## 3:15–3:50 · Soft delete and restore

1. Back as `admin`. Delete the user created earlier — the confirmation dialog names the consequence
   ("their history is kept and an administrator can restore them").
2. The row disappears from the list.
3. Tick **Show deleted users**: it is there, with when and by whom.
4. **Restore** it, and it returns to the active list.
5. Say it: *no user is ever physically deleted through the API. Deleting the same user twice answers `409`,
   not `404` — an administrator needs to tell "already gone" from "never existed".*

## 3:50–4:25 · Audit trail

1. Open **Audit**. The lifecycle just performed is there, newest first: created, updated, role changed,
   deleted, restored.
2. Expand a **Role changed** row to show the field-level diff, old value to new.
3. Point at a password-change row if one is present: the field reads **redacted**. *That a credential changed
   is what an auditor needs to see; what it changed to is what they must not.*
4. Point at the target column: it shows the username **and the user id**. *The id is the identity — usernames
   are released when a user is deleted, so the trail stays unambiguous even if the name is reused later.*

## 4:25–5:00 · Arabic, RTL, and the API

1. Click the translate button. The whole interface flips to Arabic, **right to left**, including the table,
   the paginator and the sort arrows.
2. Reload to show the language persists.
3. Switch to the Swagger tab. Show `POST /api/auth/login`, click *Authorize*, paste a token, and execute
   `GET /api/users` to make the point that the API is usable directly.
4. Finish with an error response — `GET /api/users?sortBy=passwordHash`:
   ```json
   {
     "type": "https://api.usermanagement.local/errors/invalid-sort-field",
     "title": "Invalid sort field.",
     "status": 400,
     "errorCode": "INVALID_SORT_FIELD",
     "traceId": "..."
   }
   ```
   *Structured, with a stable code the SPA and the tests branch on, a localized sentence, and a trace id that
   also appears in the logs. Sorting is a whitelist, so a column name from a client never reaches SQL.*

---

## Narration cues

For the live take. One or two sentences per beat, said while operating the application - not read aloud. The
pattern is **what, why, evidence, move on**; the demo is four to five minutes, so nothing here gets a paragraph.

| Beat | Say roughly this |
|---|---|
| **0:00 open** | "A user management module: .NET 10, Angular 21, SQL Server, Clean Architecture. Three roles, JWT with rotating refresh tokens, soft delete, auditing at the persistence layer, English and Arabic, and optimistic concurrency." Then stop talking and start clicking. |
| **Sign in** | "Passwords are hashed and salted. The access token is held in memory rather than local storage; the refresh token is a separate httpOnly cookie that rotates on every use." Show the refused sign-in: "an unknown username, a wrong password and a deleted account all get the same answer, so this cannot be used to discover whether an account exists." |
| **Create user** | "Username and email uniqueness is enforced in the database, for active users only. The form checks availability on blur as a courtesy - the database is still the authority when it is submitted." |
| **Search, sort, paging** | "List state lives in the URL, so a filtered view is shareable and the back button behaves." One sentence; move on. |
| **Soft delete** | "Delete is a soft delete. The normal query path excludes deleted users automatically, and restoring one is a separately authorized operation." On the second delete: "that is a structured 409 conflict rather than letting the state go inconsistent." |
| **Audit trail** | "Auditing runs in an EF Core interceptor at the persistence layer, so it cannot be forgotten by a caller." Then the line worth saying slowly: "audit identity is the immutable user id, not the username - usernames are released when a user is soft-deleted and can be reused by someone else." |
| **Read-only role** | "The interface hides what this role cannot do, but authorization is enforced on the API. Here is the same operation called directly - 403." That demonstration is the point; let it land. |
| **Profile** | "A user can edit their own profile and cannot change their own role. The profile contract has no role field at all." |
| **Arabic** | "English and Arabic, including right-to-left layout and localized validation and error messages." Switch, let it render, move on. |
| **Close** | "425 automated tests: backend unit and integration against real SQL Server, Angular component tests, and a Playwright browser suite covering accessibility and responsive layout - plus 66 Postman assertions. Verified from a clean clone, and the whole stack runs on one Docker command." |

**The closing number was 412 earlier in the project and is now 425** - 114 unit, 153 integration, 75 Angular,
83 browser. Say the current figure or say "over four hundred"; a number an assessor can check and find wrong is
worse than no number.

**Leave out the development story.** The `crypto.randomUUID` defect - every suite green while the application
was unusable outside localhost - does not belong in a submission demo. It is an excellent answer to "what was
the most interesting bug you found?" in an interview, because it shows the difference between tests passing and
a system being deployable. In a four-minute demo it spends time on the journey instead of the product.

Same for the rest of the defect list. The demo shows the finished thing.

---

## If you have thirty seconds more

Run the suites and let the counts speak - or, if the machine has Docker and nothing else,
`docker compose up --build` and the whole thing runs in containers:

```bash
dotnet test                     # 114 unit + 153 integration, against real SQL Server
npm test --prefix frontend      # 75
npm run e2e --prefix frontend   # 83, in a real browser
```

Then mention a defect the tests caught, because that is more convincing than a green count. Two worth having
ready:

- The HTTP interceptors were registered in the intuitive order, which - because Angular processes responses in
  reverse - meant the token-refresh interceptor saw the raw error before the mapper and tried to refresh after
  a genuinely wrong password. A test found it; the ordering is now a documented rule.
- Every suite was green while the application was, in fact, dead anywhere except localhost:
  `crypto.randomUUID` exists only in a secure context, and it was called on every request. Serving the built
  SPA from a container over plain http is what surfaced it. That is the argument for running software the way
  it will actually be run.
