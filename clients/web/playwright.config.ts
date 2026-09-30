import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end tests against the real stack: the Angular dev server (started here if needed),
 * the API on http://localhost:5209 and Keycloak on http://localhost:8080 (both must be running).
 *
 * To test the containerized app instead: E2E_BASE_URL=http://localhost:4201/en/ npm run e2e
 */
const externalBaseUrl = process.env['E2E_BASE_URL'];

export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: externalBaseUrl ?? 'http://localhost:4200/',
    trace: 'retain-on-failure',
    locale: 'en-US',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: externalBaseUrl
    ? undefined
    : {
        command: 'npm start',
        url: 'http://localhost:4200',
        reuseExistingServer: true,
        timeout: 120_000,
      },
});
