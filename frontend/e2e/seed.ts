import pg from 'pg';

/**
 * The version of the rules of ideas the scanner runs (`IdeasEngineVersion.Current`): the screens read only its calibration and
 * signals.
 */
const engineVersion = 5;

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
  /** Minutes since the idea fired; a pullback waits 4 candles of 15 minutes from then, the window of its default rule. */
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
                          status, status_at, entered_at, exit_price, move, volume_ratio, engine_version, life_candles, target_r, entry_candles)
       VALUES (gen_random_uuid(), 'linear', $1, 'impulse', $2, '15', $3, now() - make_interval(mins => $4), $5, $6, $7,
               abs($7::numeric - $5::numeric) / abs($5::numeric - $6::numeric), 'awaiting_entry', now() - interval '1 minute',
               NULL, NULL, 3, 2, 3, 24, CASE WHEN $2 = 'levels' THEN NULL ELSE 2 END,
               CASE $2 WHEN 'pullback' THEN 4 WHEN 'stop_entry' THEN 12 END)
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
 * The reports of the calibration over 90 days of all categories among all cases and after the rules changed (120 checked), as the
 * scanner stores them (decimals as strings), with the table by own data, the comparison of the models and the breakdowns by
 * timeframe and by type, so the screen has
 * tables before the scanner has history enough to check a case. The scanner replaces the reports whole when it recomputes the
 * buckets, once an hour: a test reads the screen right after this.
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
    Owns: [
      { FewOwnCases: false, Row: row({ From: null, To: null, Promised: '0.13' }), Brier: '0.205' },
      { FewOwnCases: true, Row: row({ From: null, To: null, Promised: '0.09' }), Brier: '0.212' },
    ],
    Comparison: {
      Old: {
        Cases: 600,
        Brier: '0.231',
        Up: row({ From: null, To: null, Badge: 'up', Promised: '0.145' }),
      },
      New: { Cases: 600, Brier: '0.204', Up: null },
    },
    Intervals: [
      {
        Interval: '60',
        Type: null,
        Total: row({ From: null, To: null, Promised: '0.12' }),
        Up: row({ From: null, To: null, Badge: 'up', Promised: '0.21' }),
        Comparison: {
          Old: { Cases: 600, Brier: '0.231', Up: null },
          New: { Cases: 600, Brier: '0.204', Up: null },
        },
      },
    ],
    Types: [
      {
        Interval: null,
        Type: 'pullback',
        Total: row({ From: null, To: null, Promised: '0.12' }),
        Up: null,
        Comparison: {
          Old: { Cases: 600, Brier: '0.231', Up: null },
          New: { Cases: 600, Brier: '0.204', Up: null },
        },
      },
    ],
  };

  try {
    for (const [scope, checked] of [
      ['all', 640],
      ['fresh', 120],
    ] as const) {
      await client.query(
        `INSERT INTO idea_calibration (engine_version, days, category, scope, computed_at, report)
         VALUES (${engineVersion}, 90, 'all', '${scope}', now(), $1::jsonb)
         ON CONFLICT (engine_version, days, category, scope) DO UPDATE SET computed_at = EXCLUDED.computed_at, report = EXCLUDED.report`,
        [JSON.stringify({ ...report, Checked: checked })],
      );
    }
  } finally {
    await client.end();
  }
}

/**
 * Forty signals of «Откат» long on 15 minutes of `E2E001USDT`, a pair the scanner does not keep, one a day over the last 40 days,
 * as the replay stores them: the limit at 100 fills 10 minutes after the firing, with the stop at 98; +2 R at 500 minutes from the
 * entry after +0.5, +1 and +1.5 R at 100, 200 and 350, the stop at 900, every close of the grid at 100.5. With 24 candles each
 * expires at +0.25 R before the fees, with 48 it takes +2 R.
 */
export async function seedSignals(): Promise<void> {
  const client = await connect();

  try {
    await client.query(
      `INSERT INTO idea_cases (category, symbol, setup, type, "interval", side, fired_at, engine_version, entry, stop, target, move,
                               volume_ratio, tick, entry_after, stop_before_entry_after, stop_after, ladder_after, target_after, closes,
                               tracked_until)
       SELECT 'linear', 'E2E001USDT', 'impulse', 'pullback', '15', 'long', date_trunc('minute', now()) - make_interval(days => n),
              ${engineVersion}, 100, 98, NULL, 3, 2, 0.01, 10, NULL, 900,
              ARRAY[100, 200, 350, 500, NULL, NULL, NULL, NULL, NULL, NULL]::integer[], NULL, array_fill(100.5::numeric, ARRAY[19]),
              date_trunc('minute', now()) - make_interval(days => n) + interval '14 days'
       FROM generate_series(1, 40) AS n
       ON CONFLICT DO NOTHING`,
    );
  } finally {
    await client.end();
  }
}

/** Removes the exit rules a test applied, so the ideas of the other tests live by the defaults. */
export async function removeExitRules(): Promise<void> {
  const client = await connect();

  try {
    await client.query('DELETE FROM idea_exit_rules');
  } finally {
    await client.end();
  }
}

export interface AutoEntrySeed {
  readonly ideaId: string;
  readonly autoId: string;
  readonly accountId: string;
  readonly amount: number;
  readonly minRiskReward: number;
  /** The place of the auto in the list when the idea was born: the upper auto has the lower number. */
  readonly rank: number;
}

/**
 * The entry requests of the autos with money, in the form the scanner writes them in the minute an idea is born in: the account,
 * the amount and the lowest reward to risk of the auto of that minute, its place in the list, and the order of the idea. A trading
 * Worker takes them within seconds, and a request older than a minute is refused as late, so a test seeds them right before it looks.
 */
export async function seedAutoEntries(entries: readonly AutoEntrySeed[]): Promise<void> {
  const client = await connect();

  try {
    for (const [index, entry] of entries.entries()) {
      await client.query(
        `INSERT INTO idea_auto_entries (id, idea_id, auto_id, account_id, amount, min_risk_reward, rank, fired, minute, created_at)
         VALUES (gen_random_uuid(), $1, $2, $3, $4, $5, $6, $7, date_trunc('minute', now()), now())`,
        [
          entry.ideaId,
          entry.autoId,
          entry.accountId,
          entry.amount,
          entry.minRiskReward,
          entry.rank,
          index,
        ],
      );
    }
  } finally {
    await client.end();
  }
}

/** The symbol of the real trades `seedClosedTrade` writes: no other test trades it, so they leave the rows of the others alone. */
export const closedTradeSymbol = 'XRPUSDT';

/**
 * A real trade of a pair no other test trades that closed at the stop just now with a loss of `loss` USDT after the fees: the entry
 * cost 100 and the exit brought back `100 − loss`. It counts in the loss of the day, which the daily stop of the autos reads.
 */
export async function seedClosedTrade(accountId: string, loss: number): Promise<void> {
  const client = await connect();

  try {
    await client.query(
      `INSERT INTO idea_trades (id, idea_id, account_id, category, symbol, setup, interval, type, side, entry_price, stop, target, quantity, amount, risk,
                                status, status_at, cancel_requested, close_requested, entry_quantity, entry_value, entry_fee,
                                take_profit_quantity, take_profit_value, take_profit_fee, stop_loss_quantity, stop_loss_value, stop_loss_fee,
                                manual_quantity, manual_value, manual_fee, last_order_number, created_at, lease_epoch, updated_at, opened_at)
       VALUES (gen_random_uuid(), gen_random_uuid(), $1, 'linear', $2, 'impulse', '15', 'target_2r', 'long', 100, 90, 120, 1, 100, 10,
               'closed_at_stop', now(), false, false, 1, 100, 0, 0, 0, 0, 1, 100 - $3::numeric, 0, 0, 0, 0, 2,
               now() - interval '2 hours', 0, now(), now() - interval '1 hour')`,
      [accountId, closedTradeSymbol, loss],
    );
  } finally {
    await client.end();
  }
}

/** Removes the autos, their requests, refusals and paper trades, and the closed trades `seedClosedTrade` made, so the screens are as they were. */
export async function removeAutos(): Promise<void> {
  const client = await connect();

  try {
    await client.query('DELETE FROM idea_auto_entries');
    await client.query('DELETE FROM idea_auto_refusals');
    await client.query('DELETE FROM idea_paper_trades');
    await client.query('DELETE FROM idea_trades WHERE auto_id IS NOT NULL OR symbol = $1', [
      closedTradeSymbol,
    ]);
    await client.query('DELETE FROM idea_autos');
  } finally {
    await client.end();
  }
}
