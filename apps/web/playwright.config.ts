import { defineConfig } from '@playwright/test';

/**
 * End-to-end tests run against the Angular dev server (port 4200), which proxies /api to the agent (port 5055).
 * Start the agent first with the token hash for AGENT_E2E_TOKEN (see docs/runbooks/web-ui.md).
 */
export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  reporter: [['list']],
  use: {
    baseURL: process.env['E2E_BASE_URL'] ?? 'http://localhost:4200',
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
});
