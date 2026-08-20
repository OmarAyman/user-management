# Accessibility audit

Performed during the submission-hardening pass, to close the one requirement the traceability matrix had been
carrying as **Partial**.

**Tooling:** axe-core 4.10 driven through the existing Playwright setup, scanning WCAG 2.0 A/AA and 2.1 A/AA.
Violations are asserted at **every** impact level, not filtered to serious and critical - on a surface this
small there is no reason to carry known minor breakage.

**Result:** 16 page states scanned in both text directions, **zero axe violations**, plus 11 explicit tests for
behaviours axe cannot check. Two real defects were found and fixed (section 4).

```text
npm run e2e --prefix frontend      # includes e2e/accessibility.spec.ts - 27 tests
```

---

## 1. What was scanned automatically

Each row is an axe scan of a real page state, driven through the UI rather than constructed in a test harness.

| Page state | English | Arabic (RTL) |
|---|:---:|:---:|
| Sign-in, at rest | scanned | scanned |
| Sign-in, showing field validation errors | scanned | - |
| Sign-in, showing an authentication failure | scanned | - |
| Users list, populated | scanned | scanned |
| Users list, empty state | scanned | - |
| Users list, deleted-users view | scanned | - |
| Create user form, at rest | scanned | scanned |
| Create user form, showing validation errors | scanned | - |
| Edit user form, populated | scanned | - |
| Delete confirmation dialog, open | scanned | - |
| Profile page (both cards) | scanned | scanned |
| Audit trail, populated | scanned | - |
| Forbidden page | scanned | - |

The rules that matter most on this surface, all passing: `label`, `form-field-multiple-labels`,
`aria-input-field-name`, `button-name`, `link-name`, `image-alt`, `color-contrast`, `duplicate-id-active`,
`landmark-one-main`, `region`, `list`, `table-fake-caption`, `td-headers-attr`, `th-has-data-cells`,
`aria-allowed-attr`, `aria-required-children`, `aria-valid-attr-value`, `html-has-lang`, `html-lang-valid`,
`dlitem`, `heading-order`, `frame-title`, `bypass`.

The Arabic pass is not redundant. Mirroring a layout is exactly where label association, reading order and
`dir`/`lang` correctness break, and those are machine-checkable.

## 2. What was tested explicitly, because axe cannot check it

axe inspects a rendered DOM. It cannot press keys, cannot tell whether focus moved somewhere sensible, and
cannot judge whether a message would make sense read aloud. Those are covered by named tests:

| Behaviour | Test |
|---|---|
| The whole sign-in form is reachable and submittable by keyboard alone | `the whole sign-in form is reachable and submittable by keyboard alone` |
| Keyboard focus is visibly indicated on a text field (Material's `mat-focused` state and floating label) | `keyboard focus is visibly indicated on a text field` |
| Keyboard focus on a button renders the global focus ring, measured from computed style | `keyboard focus on a button shows the focus ring` |
| The password toggle has an accessible name distinct from the field, and it changes with state | `the password toggle has a distinct accessible name from the field` |
| The confirmation dialog contains focus while open and returns it to the trigger on Escape | `the confirmation dialog traps focus and returns it to the trigger` |
| A validation error is announced and tied to its input via `aria-describedby` + `aria-invalid` | `a validation error is announced and tied to its input` |
| The users table exposes real table semantics, including `aria-sort` on the sorted column | `the users table exposes real table semantics` |
| One `main` landmark and a labelled navigation region | `the document has one main landmark and a labelled navigation region` |
| No duplicate element ids on the busiest page | `the page has no duplicate element ids` |
| Success is announced through a live region, not conveyed by colour alone | `a created user is announced through a live region` |
| Sign-in failure is announced through `role="alert"` | asserted in `accessibility - English > sign-in page with an authentication failure showing` |

## 2b. What the linter now catches before a browser runs

Added late in the same pass: `angular-eslint`'s template-accessibility rules run over every inline template
(`npm run lint`), so a whole class of defect is refused at build time rather than found by an axe scan -
the eleven rules in angular-eslint's accessibility set: `alt-text`, `click-events-have-key-events`,
`elements-content`, `interactive-supports-focus`, `label-has-associated-control`,
`mouse-events-have-key-events`, `no-autofocus`, `no-distracting-elements`, `role-has-required-aria`,
`table-scope` and `valid-aria`.

That mechanism needed proving as much as the scans did. `npm run lint:verify` lints a component whose template
contains `<div (click)="go()">` and asserts the rule reports it - because inline templates reach the linter only
through angular-eslint's processor, and a clean run over templates the linter never opened would look identical
to a clean run over templates it did.

axe and the linter answer different questions and neither replaces the other: the linter reads markup that has
not run, axe reads a rendered page with its ARIA state and computed colours.

## 3. Semantic choices worth stating

These are decisions, not defaults, and they are why the automated pass is clean:

- **Native elements first.** The table is a `<table>` with `<th>`/`<td>` and Material's sort headers as real
  `<button>`s, so `aria-sort` and header association come from the markup. Navigation is `<nav>` with `<a>`
  elements; the shell has one `<main>`.
- **ARIA only where markup cannot express it.** `role="alert"` on the sign-in failure, `role="status"` on the
  loading panel, `aria-label` on icon-only buttons, and `aria-describedby`/`aria-invalid` supplied by Material
  form fields. There is no ARIA that restates what an element already is.
- **Icons are hidden from assistive technology.** Every decorative `mat-icon` carries `aria-hidden="true"`, and
  every icon-only button carries a text label - which is what the toggle defect in section 4 was about.
- **Bidirectional text is isolated.** Usernames, emails and IP addresses are wrapped in `<bdi>` so the
  bidirectional algorithm cannot reorder them inside an Arabic layout.
- **Direction is set once**, on `<html>`, and Material reads it from there through `Directionality`. No
  component carries an RTL flag, so overlays, sort arrows and the paginator mirror without per-component work.
- **Colour is never the only signal.** Status uses a chip with text, validation uses a message, success uses a
  live-region announcement.

## 4. Defects found and fixed

| Defect | Why it mattered | Fix |
|---|---|---|
| The password-visibility button carried `aria-label="Password"` - the same accessible name as the input beside it | A screen reader announced two controls called "Password", and neither said what the button did. Found by a Playwright locator resolving to two elements, which is the same ambiguity a user would hear | Labels are now "Show password" / "Hide password", translated in both languages, and they change with state |
| The global `:focus-visible` outline never rendered on any button | Material's button styles set `outline: none`, and because they are injected after the global stylesheet a bare `:focus-visible` rule loses on equal specificity. The documented focus ring did not exist on the controls users tab through most | Element-qualified selectors raise specificity above Material's classes without `!important`. A test now measures the computed outline rather than trusting the stylesheet |

Both have regression tests. The second is the more instructive: it was documented, believed, and absent -
which is the argument for measuring computed style instead of reading CSS.

## 5. What was not automatically tested

Stated rather than implied, because "no axe violations" is not "accessible":

1. **Screen-reader narration quality.** No NVDA, JAWS or VoiceOver pass was performed. Accessible names,
   roles, live regions and focus order are asserted; whether the resulting narration is *pleasant* is a human
   judgement this pass did not make.
2. **Colour contrast beyond axe's reach.** axe checks computed foreground/background pairs, which covers the
   palette here. It cannot judge contrast over gradients or images - the interface has neither.
3. **Zoom and reflow to 400%** (WCAG 1.4.10). Layout was verified at four viewport widths down to 375px, which
   exercises the same reflow behaviour, but browser zoom itself was not scripted.
4. **`prefers-reduced-motion`.** The interface uses only Material's default transitions and no custom
   animation, so there is little to reduce - but the media query is not honoured explicitly.
5. **Keyboard traversal of every screen.** The sign-in form and the confirmation dialog are covered end to end;
   the users table and forms were checked manually rather than scripted key-by-key.
6. **Assistive-technology-specific quirks.** Material's own components are taken as accessible; this audit
   covers how the application composes them.

Items 1 and 3 are the honest gaps. Neither is claimed as verified anywhere in the documentation.
