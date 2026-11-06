import pg from 'pg';

/** The version of the rules of ideas the scanner runs (`IdeasEngineVersion.Current`): the screens read only its calibration. */
const engineVersion = 4;

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
              'target', now() - interval '1 minute', now() - make_interval(mins => n), 110 + n, 3, 2, ${engineVersion}
       FROM generate_series(1, $1::int) AS n`,
      [count],
    );
    await client.query(
      `INSERT INTO ideas (id, category, symbol, setup, type, "interval", side, fired_at, entry, stop, target, risk_reward,
                          status, status_at, entered_at, exit_price, move, volume_ratio, engine_version)
       SELECT gen_random_uuid(), category, 'BTCUSDT', 'drop', 'levels', '60', 'long',
              now() - interval '5 hours', 100, 99, 100 + reward, reward,
              'target', now() - interval '1 minute', now() - interval '5 hours', 100 + reward, 3, 2, ${engineVersion}
       FROM (VALUES ('linear', 200), ('spot', 200), ('linear', 5)) AS marker (category, reward)`,
    );
  } finally {
    await client.end();
  }
}

/**
 * A report of the calibration over 90 days of all categories, as the scanner stores it (decimals as strings), so the screen has
 * tables before the scanner has history enough to check a case. The scanner replaces the reports whole when it recomputes the
 * buckets, every 10 minutes: a test reads the screen right after this.
 */
export async function seedCalibration(): Promise<void> {
  const client = await connect();
  const row = (overrides: Record<string, unknown>) => ({
    From: '0.3',
    To: '0.4',
    Badge: null,
    Promised: '0.35',
    Targets: 218,
    Stops: 333,
    Expired: 89,
    Probability: '0.340625',
    ProbabilityLow: '0.31',
    ProbabilityHigh: '0.37',
    Expectancy: '0.02',
    ExpectancyLow: '-0.05',
    ExpectancyHigh: '0.09',
    ...overrides,
  });
  const report = {
    Checked: 640,
    Unchecked: 318,
    Brier: '0.207',
    BaseBrier: '0.219',
    Probabilities: [row({})],
    Expectancies: [row({ From: '0', To: '0.2', Promised: '0.11' })],
    Badges: [row({ From: null, To: null, Badge: 'up', Promised: '0.21' })],
    Total: row({ From: null, To: null, Promised: '0.12' }),
  };

  try {
    await client.query(
      `INSERT INTO idea_calibration (engine_version, days, category, computed_at, report) VALUES (${engineVersion}, 90, 'all', now(), $1::jsonb)
       ON CONFLICT (engine_version, days, category) DO UPDATE SET computed_at = EXCLUDED.computed_at, report = EXCLUDED.report`,
      [JSON.stringify(report)],
    );
  } finally {
    await client.end();
  }
}
