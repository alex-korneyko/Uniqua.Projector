import { defineConfig, devices } from '@playwright/test'

import { baseURL } from './e2e/support/environment.mjs'

/**
 * T31 — the e2e-through-UI flows of the test plan (test-plan.md: AC-01 + AC-12 thin path; AC-27
 * board link). They drive the built client served by the API from wwwroot against a SQL Server
 * container, on one HTTPS origin (e2e/support/app-server.mjs), so authentication behaves as it
 * ships. `vitest run` never sees these files: its `include` is `src/**\/*.test.{ts,tsx}`.
 */
export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.spec.ts',
  // One worker: the flows share one database and one source address, and registration is limited
  // per source (5 a minute), so running them side by side would only make them race that limit.
  workers: 1,
  fullyParallel: false,
  retries: 0,
  forbidOnly: !!process.env.CI,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  globalTeardown: './e2e/support/global-teardown.ts',
  use: {
    baseURL,
    // The ASP.NET development certificate is not in every browser's trust store (CI's, notably).
    ignoreHTTPSErrors: true,
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: 'node e2e/support/app-server.mjs',
    url: `${baseURL}/health`,
    ignoreHTTPSErrors: true,
    reuseExistingServer: false,
    // A container start, a client build and an API build: slow, and accepted (CLAUDE.md test floor).
    timeout: 10 * 60_000,
    stdout: 'pipe',
    stderr: 'pipe',
  },
})
