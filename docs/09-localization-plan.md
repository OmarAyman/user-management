# Localization plan

Supported cultures: **`en`** (default) and **`ar`**. Arabic renders right-to-left.

## 1. The design rule

Text is never selected by an `if (lang == "ar")` branch anywhere in the codebase. Both sides resolve a
**stable key** through a catalogue:

```text
backend   error code  ->  IStringLocalizer  ->  Messages.resx | Messages.ar.resx
frontend  i18n key    ->  Transloco         ->  assets/i18n/en.json | ar.json
```

The two catalogues meet at the error code. `ProblemDetails.errorCode` is never localized, so the SPA can map
`USERNAME_ALREADY_EXISTS` to its own Arabic copy in the same voice as the rest of the UI, while a Postman or
curl reviewer still gets a localized `title`/`detail` from the API.

## 2. Backend

### Culture resolution

`RequestLocalizationOptions` with supported cultures `en`, `ar`, default `en`, and providers in order:

1. `QueryStringRequestCultureProvider` (`?culture=ar`) — makes manual API testing trivial
2. `AcceptLanguageHeaderRequestCultureProvider` — what the SPA actually sends

An unsupported or malformed value falls back to `en` rather than erroring. The localization middleware sits
before the endpoints so validators resolve messages in the request culture.

### What is localized

| Surface | Mechanism |
|---|---|
| FluentValidation messages | `WithMessage(_ => localizer["Validation.Username.Required"])`, resolved per request |
| Business error titles and details | `IMessageLocalizer.Get(errorCode)` inside the exception handlers |
| Authentication failures | localized generic message; the code stays `INVALID_CREDENTIALS` for every cause |
| Role names | **not** localized in the API. `Role.Name` is data and part of the contract; the SPA maps `Admin` -> `مدير` for display |

### Resource layout

```text
Infrastructure/Localization/Resources/
  Messages.resx        Validation.*, Error.*, Auth.*   (English, the invariant fallback)
  Messages.ar.resx     same keys, Arabic
```

A unit test enumerates both files and fails if any key is missing from either — a missing Arabic string
silently falling back to English is the most common i18n defect, and it should break the build, not the demo.

Arabic messages are written as proper Arabic sentences, not machine-translated English word order. Numbers
inside messages use `{0}` placeholders so digit shaping stays the responsibility of the formatter.

## 3. Frontend

### Library choice

Transloco. Angular's built-in `$localize` i18n compiles one bundle per locale and needs a reload plus a
different URL to switch languages, which fails the assignment's requirement that the UI switch direction and
language interactively. Transloco loads JSON catalogues at runtime and exposes a signal-friendly API. This is
the only i18n dependency added — recorded as ADR-0007.

```text
src/assets/i18n/en.json
src/assets/i18n/ar.json
```

Keys are dotted domain paths mirroring the feature tree:

```text
common.actions.save | common.actions.cancel | common.states.loading | common.states.empty
auth.login.title | auth.login.errors.invalidCredentials
users.list.columns.username | users.list.filters.role | users.list.confirmDelete.message
users.form.fields.email | users.form.validation.emailInvalid
profile.changePassword.title
audit.list.columns.action | audit.actions.roleChange
errors.USERNAME_ALREADY_EXISTS | errors.LAST_ADMIN_CANNOT_BE_REMOVED
errors.RESOURCE_MODIFIED | errors.ACCOUNT_LOCKED | errors.unknown
```

The `errors.*` namespace is keyed by the API's error codes verbatim, so adding a backend error code means
adding two catalogue entries and nothing else. `errors.unknown` is the mandatory fallback: an unrecognised
code renders a generic localized message rather than an empty dialog, so a server-side code added ahead of a
frontend deployment degrades instead of breaking.

Two of these carry data the message must interpolate: `ACCOUNT_LOCKED` receives `retryAfterSeconds`
(rendered as minutes), and `RESOURCE_MODIFIED` prompts a reload. Both are ordinary parameterised strings, not
special cases.

### What is localized

Labels, buttons, table headers, column headers, placeholders, validation messages, empty and error states,
toasts, confirmation dialogs, navigation, browser tab titles, the role filter options, audit action names, and
`aria-label`s.

Two mechanisms keep that list honest rather than aspirational:

- **Literal text in a template is a lint error.** `@angular-eslint/template/i18n` runs with `checkText`, and
  because every string here comes from a Transloco key, any text left in markup is by definition untranslated.
  `src/index.html` is exempt: its `<title>` is what a browser tab shows before Angular boots, when there is no
  locale to translate into yet.
- **Browser tab titles go through `TranslatedTitleStrategy`** (`core/i18n/`). Route definitions carry a
  translation *key*, not a sentence - and the keys are the ones the pages already use for their headings, so a
  tab title cannot drift from the page it names. The strategy also re-applies the title on `langChanges$`,
  because switching language does not re-run the router; without that the tab would keep the previous locale's
  title for the rest of the session.

Route titles were the one piece of user-visible text outside every template, and they were literal English
until the submission-hardening pass. `src/app/route-titles.spec.ts` now asserts that each of the seven declared
titles is a dotted key that both catalogues resolve - a check the strategy alone cannot make, since literal
text resolves to itself and looks perfectly correct in an English session.

### Locale persistence and startup

`LocaleService` holds the active locale in a signal, persists it to `localStorage` under `ui.locale`
(a preference, not a credential — this is not the token-storage rule), and on bootstrap resolves in order:
stored preference -> `navigator.language` if it is Arabic -> `en`. It also sets `Accept-Language` on every
outbound request through the API client, so backend messages match the UI without a second setting.

## 4. RTL

| Concern | Approach |
|---|---|
| Document direction | `LocaleService` sets `dir` and `lang` on `<html>`; Angular Material and CDK components read direction from `Directionality`, so overlays, menus, date pickers, sort arrows and the paginator flip automatically |
| Layout | CSS **logical properties** throughout: `margin-inline-start`, `padding-inline-end`, `inset-inline-start`, `text-align: start`. No `left`/`right` in application styles, enforced by `npm run lint:styles` over both `.scss` files and inline component styles; an exception needs an explicit `/* physical: reason */` marker on the line |
| Icons | Directional icons (back/forward, chevrons) flip with `transform: scaleX(-1)` under `[dir="rtl"]`; non-directional icons (delete, edit) never flip |
| Tables | Column order reverses naturally with `dir`; numeric columns keep `text-align: end` in both directions so figures stay aligned |
| Numbers and dates | `Intl` formatting through the locale; Arabic uses `ar` formatting with Latin digits (`ar-u-nu-latn`) because the audit trail and IP addresses are read alongside English logs. Recorded as ADR-0007 |
| Forms | Material form fields, hints and error text mirror automatically; input `dir="auto"` on email and username fields so Latin-script values stay readable inside an RTL layout |
| Mixed content | Emails, usernames and IP addresses are wrapped in a `<bdi>` element so bidirectional reordering cannot mangle them |

## 5. Verification

| Check | How |
|---|---|
| No missing Arabic keys (backend) | resx parity unit test |
| No missing Arabic keys (frontend) | catalogue parity test comparing the key sets of `en.json` and `ar.json` |
| Localized API errors | integration test posting an invalid user with `Accept-Language: ar` and asserting an Arabic `title` plus an unchanged `errorCode` |
| Direction switching | component test asserting `document.documentElement.dir` becomes `rtl` after switching to Arabic and back |
| No hard-coded strings | `@angular-eslint/template/i18n` with `checkText` (`npm run lint`), proven to fire by `npm run lint:verify` |
| No literal route titles | `route-titles.spec.ts`: every declared title is a dotted key resolving in both catalogues |
| Tab title follows the language | `translated-title.strategy.spec.ts`: navigates for real, switches to Arabic, and asserts the title changes without a second navigation |
| No physical direction properties | `npm run lint:styles` over `.scss` and inline component styles |
| Visual RTL sanity | manual pass on Login, Users, Create/Edit, Profile and Audit at desktop and mobile widths, captured in the Phase 8 evidence notes |

## 6. Known limitations

- Only `en` and `ar` are shipped. Adding a locale means one resx pair plus one JSON file; no code changes.
- Arabic copy is authored, not professionally reviewed; a native-speaker review is listed in the README's
  known limitations.
- Pluralisation uses Transloco's message format for the few plural strings (result counts); Arabic's six
  plural forms are handled by the library rather than by hand-written branches.
