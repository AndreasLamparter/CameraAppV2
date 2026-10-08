import js from '@eslint/js'
import tseslint from 'typescript-eslint'
import vue from 'eslint-plugin-vue'
import prettier from 'eslint-config-prettier'

export default tseslint.config(
  { ignores: ['auto-imports.d.ts', 'components.d.ts', '.nuxt-ui', 'node_modules', 'src/api/generated', 'dist', 'test-results', 'playwright-report'] },
  js.configs.recommended,
  ...tseslint.configs.strict,
  ...vue.configs['flat/recommended'],
  {
    files: ['**/*.vue'],
    languageOptions: { parserOptions: { parser: tseslint.parser, extraFileExtensions: ['.vue'] } },
  },
  {
    rules: {
      // TypeScript checks undefined identifiers (browser globals included).
      'no-undef': 'off',
      '@typescript-eslint/no-explicit-any': 'error',
      'vue/no-v-html': 'error',
      'vue/multi-word-component-names': 'off',
    },
  },
  prettier,
)
