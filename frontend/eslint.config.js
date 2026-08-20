// @ts-check
const eslint = require('@eslint/js');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');
const importX = require('eslint-plugin-import-x');

/**
 * The four top-level boundaries (`core`, `shared`, `layout`, `features`) only mean something if crossing them
 * fails a build rather than a review. The backend has architecture tests for exactly this; this is the
 * frontend's equivalent, and the direction table below is the one in docs/03-project-structure.md.
 *
 * `import-x/no-restricted-paths` is used rather than `no-restricted-imports` because it resolves the import to
 * a real path first. That matters: a cross-feature import written from inside a feature reads `../audit/page`,
 * with no `features/` segment for a string pattern to match on, so pattern matching would let through the one
 * rule most likely to be broken by accident.
 */
const boundaries = [
  // core is the bottom of the stack: it may not reach up into presentation or a feature.
  { target: './src/app/core', from: './src/app/shared' },
  { target: './src/app/core', from: './src/app/features' },
  { target: './src/app/core', from: './src/app/layout' },

  // shared holds presentation-only building blocks. It may use core models and typed HTTP contracts, but
  // pulling in a data service would make a "shared" component quietly feature-aware.
  { target: './src/app/shared', from: './src/app/features' },
  { target: './src/app/shared', from: './src/app/layout' },
  { target: './src/app/shared', from: './src/app/core/services' },

  // layout composes the shell from core and shared; a feature belongs behind a route, not in the chrome.
  { target: './src/app/layout', from: './src/app/features' },

  // No feature may import a sibling feature. Shared behaviour moves down into core or shared instead.
  { target: './src/app/features/audit', from: './src/app/features', except: ['./audit'] },
  { target: './src/app/features/auth', from: './src/app/features', except: ['./auth'] },
  { target: './src/app/features/errors', from: './src/app/features', except: ['./errors'] },
  { target: './src/app/features/profile', from: './src/app/features', except: ['./profile'] },
  { target: './src/app/features/users', from: './src/app/features', except: ['./users'] },
];

module.exports = tseslint.config(
  {
    ignores: ['dist/**', 'out-tsc/**', 'coverage/**', 'playwright-report/**', 'test-results/**', '.angular/**'],
  },
  {
    files: ['**/*.ts'],
    plugins: { 'import-x': importX },
    settings: {
      // Without a resolver that knows about `.ts`, an extensionless relative import never resolves and
      // `no-restricted-paths` silently passes every file - which is exactly what happened on the first run, and
      // is why scripts/verify-lint-rules.mjs exists.
      'import-x/resolver-next': [
        importX.createNodeResolver({ extensions: ['.ts', '.d.ts', '.js', '.mjs', '.json'] }),
      ],
    },
    extends: [
      eslint.configs.recommended,
      ...tseslint.configs.recommended,
      ...tseslint.configs.stylistic,
      ...angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': ['error', { type: 'attribute', prefix: 'app', style: 'camelCase' }],
      '@angular-eslint/component-selector': ['error', { type: 'element', prefix: 'app', style: 'kebab-case' }],

      'import-x/no-restricted-paths': ['error', { basePath: __dirname, zones: boundaries }],

      // Angular's sanitizer is the last defence against XSS in a template, and docs/08-security-plan.md says
      // bypassing it is banned. A ban that only exists in prose is not a control.
      'no-restricted-syntax': [
        'error',
        {
          selector: 'MemberExpression[property.name=/^bypassSecurityTrust/]',
          message:
            'bypassSecurityTrust* disables Angular sanitization. Render text, or bind a URL through a typed, validated value instead.',
        },
        {
          selector: "MemberExpression[object.name='localStorage'][property.name='setItem']",
          message:
            'Do not write to localStorage. The access token is held in memory by design (docs/08-security-plan.md T-03); only LocaleService may persist the UI language, and it does so through its own guarded path.',
        },
      ],

      // Unused code is either a mistake or noise; either way a reviewer should not be the one to find it.
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_', caughtErrors: 'all' }],
    },
  },
  {
    // LocaleService is the one place allowed to persist anything, and it is covered by tests that assert the
    // stored value is the UI language and nothing else.
    files: ['src/app/core/services/locale.service.ts'],
    rules: { 'no-restricted-syntax': 'off' },
  },
  {
    // Specs and the test bootstrap legitimately manipulate storage and assert on it.
    files: ['**/*.spec.ts', 'src/test-setup.ts', 'e2e/**/*.ts'],
    rules: {
      'no-restricted-syntax': 'off',
      '@typescript-eslint/no-explicit-any': 'off',
    },
  },
  {
    files: ['**/*.html'],
    // index.html is the static shell, not a component template: its <title> is what a browser tab shows before
    // Angular boots and the title strategy sets a translated one. There is no locale to translate it into yet.
    ignores: ['src/index.html'],
    extends: [...angular.configs.templateRecommended, ...angular.configs.templateAccessibility],
    rules: {
      // Every user-visible string goes through Transloco, so literal text in a template is an untranslated
      // string. The rule's own remedy (Angular `i18n` attributes) is not the one used here - the finding is
      // what matters, and the fix is a translation key.
      '@angular-eslint/template/i18n': [
        'error',
        {
          checkText: true,
          checkAttributes: false,
          checkId: false,
          ignoreTags: ['mat-icon'],
        },
      ],
    },
  },
);
