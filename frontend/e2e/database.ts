// Creates or drops the disposable database of an end-to-end run: node e2e/database.ts create|drop <name>.
// The server comes from PROFITFOX_E2E_POSTGRES (a connection string to its `postgres` database); by default the
// local development database of docker-compose.yml, whose credentials are not a secret.
import pg from 'pg';

const [action, name] = process.argv.slice(2);
const server =
  process.env.PROFITFOX_E2E_POSTGRES ?? 'postgres://profitfox:profitfox@127.0.0.1:5432/postgres';

if (!name || !/^[a-z0-9_]+$/.test(name) || (action !== 'create' && action !== 'drop')) {
  throw new Error(
    'Usage: node e2e/database.ts create|drop <name of lower-case letters, digits and _>',
  );
}

const client = new pg.Client({ connectionString: server });
await client.connect();

try {
  await client.query(
    action === 'create'
      ? `CREATE DATABASE ${name}`
      : `DROP DATABASE IF EXISTS ${name} WITH (FORCE)`,
  );
} finally {
  await client.end();
}
