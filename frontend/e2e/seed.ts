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

/** What an open idea of the trades needs: the pair, the type and the side, the levels, and how long ago it fired. */
export interface OpenIdea {
  readonly symbol: string;
  readonly type: string;
  readonly side: 'long' | 'short';
  readonly entry: number;
  readonly stop: number;
  readonly target: number;
  /** Minutes since the idea fired; a pullback lives 4 candles of 15 minutes from then. */
  readonly firedMinutesAgo: number;
}

/**
 * An idea of the linear market that waits for its entry, of the setup «Импульс» on 15 minutes. The ideas of the same pair, type and side
 * that are still open are removed first, as the database keeps one open idea of a kind. Gives its id.
 */
export async function seedOpenIdea(idea: OpenIdea): Promise<string> {
  const client = await connect();

  try {
    await client.query(
      `DELETE FROM ideas WHERE category = 'linear' AND symbol = $1 AND setup = 'impulse' AND type = $2 AND "interval" = '15'
         AND side = $3 AND status IN ('awaiting_entry', 'active')`,
      [idea.symbol, idea.type, idea.side],
    );
    const inserted = await client.query<{ id: string }>(
      `INSERT INTO ideas (id, category, symbol, setup, type, "interval", side, fired_at, entry, stop, target, risk_reward,
                          status, status_at, entered_at, exit_price, move, volume_ratio, engine_version)
       VALUES (gen_random_uuid(), 'linear', $1, 'impulse', $2, '15', $3, now() - make_interval(mins => $4), $5, $6, $7,
               abs($7::numeric - $5::numeric) / abs($5::numeric - $6::numeric), 'awaiting_entry', now() - interval '1 minute',
               NULL, NULL, 3, 2, 3)
       RETURNING id`,
      [idea.symbol, idea.type, idea.side, idea.firedMinutesAgo, idea.entry, idea.stop, idea.target],
    );

    return inserted.rows[0]!.id;
  } finally {
    await client.end();
  }
}

/** Removes the ideas a test seeded for a pair, so the lists of the other tests are as they were. */
export async function removeOpenIdeas(symbol: string): Promise<void> {
  const client = await connect();

  try {
    await client.query(
      `DELETE FROM ideas WHERE category = 'linear' AND symbol = $1 AND setup = 'impulse' AND "interval" = '15'
         AND type IN ('levels', 'pullback')`,
      [symbol],
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
