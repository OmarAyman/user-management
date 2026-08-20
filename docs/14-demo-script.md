# Five-minute demo script

A recording plan. Every claim below is something the running application actually does — no slides, no
narration over a static screen.

**Before you record:** start the API (`dotnet run --project src/UserManagement.Api --urls http://localhost:5080`)
and the SPA (`npm start` in `frontend/`), and have Swagger open in a second tab. Sign out first, so the
recording starts at the sign-in screen.

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

## 2:45–3:30 · User profile

1. Still as `readonly`, open **Profile**. Update the first name and save.
2. Point at what is **not** there: no role control, and the note explaining that an administrator sets it.
   *The profile model has no role field at all, so self-elevation is not something the UI declines — it is
   something the contract cannot express.*
3. Optional, if time allows: change the password, then show that the session ends and a fresh sign-in with the
   new password works.

## 3:30–4:00 · Soft delete

1. Back as `admin`. Delete the user created earlier — the confirmation dialog names the consequence
   ("their history is kept and an administrator can restore them").
2. The row disappears from the list.
3. Tick **Show deleted users**: it is there, with when and by whom.
4. **Restore** it, and it returns to the active list.
5. Say it: *no user is ever physically deleted through the API. Deleting the same user twice answers `409`,
   not `404` — an administrator needs to tell "already gone" from "never existed".*

## 4:00–4:30 · Audit trail

1. Open **Audit**. The lifecycle just performed is there, newest first: created, updated, role changed,
   deleted, restored.
2. Expand a **Role changed** row to show the field-level diff, old value to new.
3. Point at a password-change row if one is present: the field reads **redacted**. *That a credential changed
   is what an auditor needs to see; what it changed to is what they must not.*
4. Point at the target column: it shows the username **and the user id**. *The id is the identity — usernames
   are released when a user is deleted, so the trail stays unambiguous even if the name is reused later.*

## 4:30–5:00 · Arabic, RTL, and the API

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

## If you have thirty seconds more

Run the two commands and let the counts speak:

```bash
dotnet test          # 94 unit + 145 integration, against real SQL Server
npm test --prefix frontend
```

Then mention one defect the tests caught, because that is more convincing than a green count: the frontend's
HTTP interceptors were registered in the intuitive order, which — because Angular processes responses in
reverse — meant the token-refresh interceptor saw the raw error before the mapper and tried to refresh after a
genuinely wrong password. A test found it; the ordering is now a documented rule.
