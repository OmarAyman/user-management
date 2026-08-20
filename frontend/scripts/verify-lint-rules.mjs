/**
 * Proves the architectural lint rules actually fire.
 *
 * `eslint .` reporting zero problems is ambiguous: it means either the code obeys the boundaries or the rules
 * never ran. The backend settles that question with architecture tests that assert on a violation; this is the
 * frontend equivalent. Each case below is a deliberate violation linted through ESLint's Node API, and the
 * script fails if the expected rule stays silent - including one negative control, so a rule that fires on
 * everything is caught too.
 *
 * Run with `npm run lint:verify` (and it runs in the same CI step as `npm run lint`).
 */

import { ESLint } from 'eslint';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const eslint = new ESLint({ cwd: root });

/** @type {{name: string, filePath: string, code: string, expect: string | null}[]} */
const cases = [
  {
    name: 'core may not import a feature',
    filePath: 'src/app/core/services/boundary-probe.ts',
    code: "import { UsersListPage } from '../../features/users/users-list.page';\nexport const probe = UsersListPage;\n",
    expect: 'import-x/no-restricted-paths',
  },
  {
    name: 'core may not import shared',
    filePath: 'src/app/core/services/boundary-probe.ts',
    code: "import { ConfirmDialog } from '../../shared/components/confirm-dialog';\nexport const probe = ConfirmDialog;\n",
    expect: 'import-x/no-restricted-paths',
  },
  {
    name: 'shared may not import a core service',
    filePath: 'src/app/shared/components/boundary-probe.ts',
    code: "import { UsersApiService } from '../../core/services/users-api.service';\nexport const probe = UsersApiService;\n",
    expect: 'import-x/no-restricted-paths',
  },
  {
    name: 'layout may not import a feature',
    filePath: 'src/app/layout/boundary-probe.ts',
    code: "import { AuditPage } from '../features/audit/audit.page';\nexport const probe = AuditPage;\n",
    expect: 'import-x/no-restricted-paths',
  },
  {
    // The case string patterns cannot see: written from inside a feature, a sibling import carries no
    // "features/" segment at all.
    name: 'a feature may not import a sibling feature',
    filePath: 'src/app/features/users/boundary-probe.ts',
    code: "import { AuditPage } from '../audit/audit.page';\nexport const probe = AuditPage;\n",
    expect: 'import-x/no-restricted-paths',
  },
  {
    name: 'bypassing Angular sanitization is refused',
    filePath: 'src/app/features/users/boundary-probe.ts',
    code: "export function render(sanitizer: { bypassSecurityTrustHtml(value: string): unknown }, value: string) {\n  return sanitizer.bypassSecurityTrustHtml(value);\n}\n",
    expect: 'no-restricted-syntax',
  },
  {
    name: 'writing to localStorage outside LocaleService is refused',
    filePath: 'src/app/features/users/boundary-probe.ts',
    code: "export function remember(token: string) {\n  localStorage.setItem('token', token);\n}\n",
    expect: 'no-restricted-syntax',
  },
  {
    // Inline templates are linted through angular-eslint's processor. Without this case a clean run would not
    // distinguish "the templates are accessible" from "template rules never looked at them".
    name: 'template accessibility rules reach inline templates',
    filePath: 'src/app/features/users/boundary-probe.ts',
    code: "import { Component } from '@angular/core';\n\n@Component({\n  selector: 'app-boundary-probe',\n  template: '<div (click)=\"go()\">Go</div>',\n})\nexport class BoundaryProbe {\n  go() {}\n}\n",
    expect: '@angular-eslint/template/click-events-have-key-events',
  },
  {
    // Negative control. A feature reaching down into core is the intended direction, and a rule that flags it
    // would be worse than no rule at all.
    name: 'a feature may import a core service',
    filePath: 'src/app/features/users/boundary-probe.ts',
    code: "import { UsersApiService } from '../../core/services/users-api.service';\nexport const probe = UsersApiService;\n",
    expect: null,
  },
];

let failures = 0;

for (const testCase of cases) {
  const [result] = await eslint.lintText(testCase.code, {
    filePath: path.join(root, testCase.filePath),
  });

  const ruleIds = (result?.messages ?? []).map((message) => message.ruleId);

  if (testCase.expect === null) {
    const boundaryComplaints = ruleIds.filter(
      (id) => id === 'import-x/no-restricted-paths' || id === 'no-restricted-syntax',
    );

    if (boundaryComplaints.length > 0) {
      failures += 1;
      console.error(`FAIL  ${testCase.name}\n      expected no boundary error, got: ${boundaryComplaints.join(', ')}`);
      continue;
    }

    console.log(`ok    ${testCase.name}`);
    continue;
  }

  if (!ruleIds.includes(testCase.expect)) {
    failures += 1;
    console.error(
      `FAIL  ${testCase.name}\n      expected ${testCase.expect}, got: ${ruleIds.length === 0 ? '(no problems reported)' : ruleIds.join(', ')}`,
    );
    continue;
  }

  console.log(`ok    ${testCase.name}`);
}

if (failures > 0) {
  console.error(`\n${failures} of ${cases.length} lint-rule checks failed. The boundaries are not being enforced.`);
  process.exit(1);
}

console.log(`\n${cases.length} lint-rule checks passed.`);
