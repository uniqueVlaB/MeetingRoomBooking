// @ts-check
const eslint = require('@eslint/js');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

/**
 * Lint rules for the client.
 *
 * The backend has had warnings-as-errors from the start; this is the other half of that bargain.
 * Rules are kept to ones that catch a real mistake rather than ones that police taste — formatting
 * is Prettier's job, and duplicating it here would only produce two sources of truth that disagree.
 */
module.exports = tseslint.config(
  {
    // Build output and caches, not source. Linting them reports on code we did not write.
    ignores: ['dist/**', '.angular/**', 'coverage/**', 'out-tsc/**'],
  },
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      ...tseslint.configs.recommended,
      ...tseslint.configs.stylistic,
      ...angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix: 'app', style: 'camelCase' },
      ],
      '@angular-eslint/component-selector': [
        'error',
        { type: 'element', prefix: 'app', style: 'kebab-case' },
      ],

      // A floating promise in this codebase means an unawaited booking or an unhandled load error,
      // which is exactly the class of bug that leaves a schedule looking wrong with no sign of why.
      '@typescript-eslint/no-floating-promises': 'error',

      // Deliberately unused parameters are named with a leading underscore, as in `_route`.
      '@typescript-eslint/no-unused-vars': [
        'error',
        { argsIgnorePattern: '^_', varsIgnorePattern: '^_' },
      ],
    },
    languageOptions: {
      parserOptions: {
        projectService: true,
        tsconfigRootDir: __dirname,
      },
    },
  },
  {
    files: ['**/*.html'],
    extends: [...angular.configs.templateRecommended, ...angular.configs.templateAccessibility],
    rules: {},
  },
);
