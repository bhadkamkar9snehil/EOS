import { defineConfig } from '@playwright/test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const root = path.resolve(__dirname, '..', '..');
const port = 8096;

function installedChromium(): string | undefined {
  const base = path.join(process.env.LOCALAPPDATA ?? '', 'ms-playwright');
  try {
    const dirs = fs.readdirSync(base).filter((d) => /^chromium-\d+$/.test(d)).sort().reverse();
    for (const d of dirs) {
      const exe = path.join(base, d, 'chrome-win64', 'chrome.exe');
      if (fs.existsSync(exe)) return exe;
    }
  } catch { }
  return undefined;
}

const browser = process.env.EOS_E2E_BROWSER ?? installedChromium();

export default defineConfig({
  testDir: './specs',
  timeout: 45_000,
  expect: { timeout: 8_000 },
  fullyParallel: true,
  workers: 4,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    headless: true,
    viewport: { width: 1500, height: 900 },
    permissions: ['clipboard-read', 'clipboard-write'],
    trace: 'retain-on-failure',
    launchOptions: browser ? { executablePath: browser } : {},
  },
  webServer: {
    command: `node tests/e2e/prepare-db.js && dotnet run --project src/EOS.Server -c Debug --no-launch-profile`,
    cwd: root,
    url: `http://127.0.0.1:${port}/health`,
    timeout: 240_000,
    reuseExistingServer: false,
    stdout: 'ignore',
    env: {
      EOS_DEV_NOAUTH: '1',
      EOS_DB: path.join(os.tmpdir(), 'eos-e2e.db'),
      ASPNETCORE_URLS: `http://127.0.0.1:${port}`,
      ASPNETCORE_ENVIRONMENT: 'Development',
    },
  },
});
