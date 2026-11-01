import js from '@eslint/js';
import prettier from 'eslint-config-prettier';
import simpleImportSort from 'eslint-plugin-simple-import-sort';
import tseslint from 'typescript-eslint';

/** Builds a layer rule: imports matching any of the regular expressions are rejected. */
function layer(files, restrictions) {
  return {
    files,
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: restrictions.map(([regex, message]) => ({ regex, message })),
        },
      ],
    },
  };
}

const deepModuleImport = [
  '^(@/|(\\.\\./)+)modules/[^/]+/.+',
  'Import another module only through its index.ts: @/modules/<name>.',
];

export default tseslint.config(
  { ignores: ['node_modules/**', 'dist/**', 'test-results/**', 'src/shared/api/generated/**'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  prettier,
  {
    plugins: { 'simple-import-sort': simpleImportSort },
    rules: {
      curly: ['error', 'all'],
      'no-var': 'error',
      'prefer-const': 'error',
      // Leaving a key out with `const { key: _, ...rest } = object` is how an object loses a key.
      '@typescript-eslint/no-unused-vars': ['error', { ignoreRestSiblings: true }],
      'simple-import-sort/imports': 'error',
      'simple-import-sort/exports': 'error',
      'no-restricted-syntax': [
        'error',
        {
          selector: 'Program > :not(ImportDeclaration) ~ ImportDeclaration',
          message: 'Place imports before other statements.',
        },
      ],
    },
  },
  layer(
    ['src/shared/**'],
    [['^(@/|(\\.\\./)+)(modules|pages|app)(/|$)', 'shared must not import modules, pages or app.']],
  ),
  layer(
    ['src/modules/**'],
    [
      ['^(@/|(\\.\\./)+)(pages|app)(/|$)', 'modules must not import pages or app.'],
      deepModuleImport,
    ],
  ),
  layer(
    ['src/pages/**'],
    [
      ['^(@/|(\\.\\./)+)app(/|$)', 'pages must not import app.'],
      ['^@/pages(/|$)', 'A page must not import another page.'],
      deepModuleImport,
    ],
  ),
);
