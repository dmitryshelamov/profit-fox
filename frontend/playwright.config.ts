import { randomBytes } from 'node:crypto';

import { defineConfig, devices } from '@playwright/test';

// The runner evaluates this file once and its workers inherit the environment, so the whole run shares one identity.
process.env.PROFITFOX_E2E_DATABASE ??= `profitfox_e2e_${randomBytes(6).toString('hex')}`;
process.env.PROFITFOX_E2E_WEB_CONTAINER ??= `profit-fox-e2e-web-${randomBytes(4).toString('hex')}`;

const database = process.env.PROFITFOX_E2E_DATABASE;
const container = process.env.PROFITFOX_E2E_WEB_CONTAINER;
const apiPort = 5293;
const webPort = 5275;
const postgres = process.env.PROFITFOX_E2E_POSTGRES_HOST ?? '127.0.0.1:5432';
const [host, port] = postgres.split(':');

// Linux (CI) runs the web container in the host network; Docker Desktop reaches the host by name instead.
const linux = process.platform === 'linux';
const network = linux
  ? `--network host -e PROFITFOX_WEB_PORT=${webPort} -e PROFITFOX_API=127.0.0.1:${apiPort}`
  : `-p 127.0.0.1:${webPort}:80 -e PROFITFOX_API=host.docker.internal:${apiPort}`;

const api = [
  `node e2e/database.ts create ${database} &&`,
  'ASPNETCORE_ENVIRONMENT=Testing',
  `ConnectionStrings__MarketData="Host=${host};Port=${port};Database=${database};Username=profitfox;Password=profitfox"`,
  `dotnet run --no-launch-profile --project ../backend/src/ProfitFox.Presentation --urls http://127.0.0.1:${apiPort}`,
].join(' ');

/**
 * End-to-end tests drive the interface through the web image (Caddy) against the real API on a disposable database,
 * dropped afterwards. No Worker runs: the editor does not need one, and nothing reaches the exchange.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  globalTeardown: './e2e/global-teardown.ts',
  use: {
    ...devices['Desktop Chrome'],
    baseURL: `http://127.0.0.1:${webPort}`,
    timezoneId: 'UTC',
    viewport: { width: 1440, height: 900 },
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  webServer: [
    {
      command: api,
      url: `http://127.0.0.1:${apiPort}/openapi/v1.json`,
      reuseExistingServer: false,
      timeout: 180_000,
    },
    {
      command: `docker build -q -t profit-fox-web . && docker run --rm --name ${container} ${network} profit-fox-web`,
      url: `http://127.0.0.1:${webPort}/strategies`,
      reuseExistingServer: false,
      timeout: 300_000,
    },
  ],
});
