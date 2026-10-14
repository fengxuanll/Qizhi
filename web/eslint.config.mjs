import vue from 'eslint-plugin-vue'
import ts from 'typescript-eslint'
import prettier from 'eslint-config-prettier'

export default [
  { ignores: ['dist/**', 'node_modules/**', 'coverage/**'] },
  ...vue.configs['flat/recommended'],
  { files: ['src/**/*.ts'], languageOptions: { parser: ts.parser } },
  {
    files: ['src/**/*.{ts,vue}'],
    languageOptions: { parserOptions: { parser: ts.parser } },
    plugins: { '@typescript-eslint': ts.plugin },
    rules: {
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
      'vue/multi-word-component-names': 'off',
    },
  },
  prettier,
]
