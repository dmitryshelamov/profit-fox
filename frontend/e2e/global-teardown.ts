import { execFileSync } from 'node:child_process';

/** Drops the disposable database and removes the web container, even when a run fails. */
export default function teardown() {
  execFileSync('docker', ['rm', '-f', process.env.PROFITFOX_E2E_WEB_CONTAINER!], {
    stdio: 'ignore',
  });
  execFileSync('node', ['e2e/database.ts', 'drop', process.env.PROFITFOX_E2E_DATABASE!], {
    stdio: 'inherit',
  });
}
