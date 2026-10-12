// AI: конфігурація ESLint з eslint-plugin-security для MCP-сервера (issue #62); перевірено: npm run lint, самоперевірка автора (§8)

import js from '@eslint/js';
import { defineConfig } from 'eslint/config';
import pluginSecurity from 'eslint-plugin-security';
import tseslint from 'typescript-eslint';

export default defineConfig(
  { ignores: ['build/**', 'node_modules/**'] },
  js.configs.recommended,
  tseslint.configs.recommended,
  // Аналізатор безпеки: eval, небезпечні регулярні вирази, child_process, доступ до файлів за змінним шляхом тощо
  pluginSecurity.configs.recommended,
  {
    files: ['src/**/*.ts'],
    rules: {
      // Правила перенесено з .eslintrc.json, яку створив генератор MCP-сервера
      'no-console': ['error', { allow: ['error', 'warn'] }],
      '@typescript-eslint/explicit-function-return-type': 'off',
      '@typescript-eslint/no-explicit-any': 'off',
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
    },
  },
);
