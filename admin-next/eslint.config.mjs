import nextPlugin from "@next/eslint-plugin-next";
import tsParser from "@typescript-eslint/parser";
import tsPlugin from "@typescript-eslint/eslint-plugin";

// Flat config built directly from the Next.js ESLint plugin + typescript-eslint.
// eslint-config-next@16.2.6 ships a broken package export: both its classic and
// flat entrypoints resolve to "eslint-config-next/dist/flat/core-web-vitals",
// which does not exist, so we wire the underlying plugins ourselves.
const eslintConfig = [
  {
    ignores: [".next/**", "out/**", "build/**", "node_modules/**", "next-env.d.ts"],
  },
  {
    files: ["**/*.{ts,tsx}"],
    languageOptions: {
      parser: tsParser,
      parserOptions: { ecmaFeatures: { jsx: true } },
    },
    plugins: {
      "@next/next": nextPlugin,
      "@typescript-eslint": tsPlugin,
    },
    rules: {
      ...nextPlugin.configs.recommended.rules,
      ...nextPlugin.configs["core-web-vitals"].rules,
      ...tsPlugin.configs.recommended.rules,
      // TypeScript's own checker covers undefined identifiers; the core rule
      // produces false positives on TS-only type references.
      "no-undef": "off",
    },
  },
];

export default eslintConfig;
