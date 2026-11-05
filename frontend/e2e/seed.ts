import pg from 'pg';

/** A client of the disposable database of the run, whose name playwright.config.ts gave to every worker. */
async function connect(): Promise<pg.Client> {
  const server = new URL(
    process.env.PROFITFOX_E2E_POSTGRES ?? 'postgres://profitfox:profitfox@127.0.0.1:5432/postgres',
  );
  server.pathname = `/${process.env.PROFITFOX_E2E_DATABASE!}`;
  const client = new pg.Client({ connectionString: server.toString() });
  await client.connect();

  return client;
}

/**
 * Closed ideas of a day's list, which the scanner never touches, so a list is long enough to have pages. `count` ideas of
 * pairs the market does not have, `E2E001USDT` the newest; the risk to reward of the n-th is 10 + n, so the oldest has the
 * highest. Then three of a pair the market has: a risk to reward of 200 on linear and on spot, and of 5 on linear.
 */
export async function seedIdeas(count: number): Promise<void> {
  const client = await connect();

  try {
    await client.query(
      `INSERT INTO ideas (id, category, symbol, setup, type, "interval", side, fired_at, entry, stop, target, risk_reward,
                          status, status_at, entered_at, exit_price, move, volume_ratio, engine_version)
       SELECT gen_random_uuid(), 'linear', 'E2E' || lpad(n::text, 3, '0') || 'USDT', 'drop', 'levels', '60', 'long',
              now() - make_interval(mins => n), 100, 99, 110 + n, 10 + n,
              'target', now() - interval '1 minute', now() - make_interval(mins => n), 110 + n, 3, 2, 3
       FROM generate_series(1, $1::int) AS n`,
      [count],
    );
    await client.query(
      `INSERT INTO ideas (id, category, symbol, setup, type, "interval", side, fired_at, entry, stop, target, risk_reward,
                          status, status_at, entered_at, exit_price, move, volume_ratio, engine_version)
       SELECT gen_random_uuid(), category, 'BTCUSDT', 'drop', 'levels', '60', 'long',
              now() - interval '5 hours', 100, 99, 100 + reward, reward,
              'target', now() - interval '1 minute', now() - interval '5 hours', 100 + reward, 3, 2, 3
       FROM (VALUES ('linear', 200), ('spot', 200), ('linear', 5)) AS marker (category, reward)`,
    );
  } finally {
    await client.end();
  }
}
