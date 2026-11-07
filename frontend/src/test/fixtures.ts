import type { Schemas } from '@/shared/api/client';

export const emaSummary = {
  id: '11111111-1111-4111-8111-111111111111',
  name: 'EMA',
  direction: 'long',
  supportedCategories: ['spot', 'linear'],
};

export const ladderSummary = {
  id: '22222222-2222-4222-8222-222222222222',
  name: 'Ladder',
  direction: 'long',
  supportedCategories: ['spot', 'linear'],
};

export const emaShortSummary = {
  id: '33333333-3333-4333-8333-333333333333',
  name: 'EMA Short',
  direction: 'short',
  supportedCategories: ['linear'],
};

export const trendFilter = {
  id: '44444444-4444-4444-8444-444444444444',
  title: 'Фильтр тренда',
  group: 'custom',
  inputs: [{ name: 'source', type: 'number' }],
  outputs: [{ name: 'aboveTrend', type: 'boolean' }],
  settings: [],
  definition: null,
};

export function savedStrategy(
  summary: { id: string; name: string; direction: string },
  definition: unknown,
) {
  return {
    ...summary,
    supportedCategories: summary.direction === 'short' ? ['linear'] : ['spot', 'linear'],
    definition,
    version: 1,
    createdAt: '2026-11-01T10:00:00Z',
    updatedAt: '2026-11-01T10:00:00Z',
  };
}

export const trendFilterDefinition = {
  formatVersion: 1,
  inputs: [{ name: 'source', type: 'number' }],
  outputs: [{ name: 'aboveTrend', type: 'boolean' }],
  nodes: [
    { id: 'in', kind: 'inputs' },
    { id: 'ema', kind: 'ema', inputs: { period: 200 } },
    { id: 'above', kind: 'compare', settings: { operation: 'gt' } },
    { id: 'out', kind: 'outputs' },
  ],
  wires: [
    { from: 'in.source', to: 'ema.source' },
    { from: 'in.source', to: 'above.a' },
    { from: 'ema.value', to: 'above.b' },
    { from: 'above.result', to: 'out.aboveTrend' },
  ],
  layout: { in: { x: 0, y: 0 }, out: { x: 600, y: 0 } },
};

export const emaDefinition = {
  formatVersion: 1,
  direction: 'long',
  parameters: [
    { name: 'fast', type: 'number', default: 12, min: 2, max: 200, integer: true },
    { name: 'slow', type: 'number', default: 26, min: 2, max: 400, integer: true },
    { name: 'cooldown', type: 'duration', default: '04:00:00' },
  ],
  nodes: [],
};

export const progressOf = (calculated: number, total: number) => ({
  loadedCandles: 100,
  expectedCandles: 100,
  calculatedCombinations: calculated,
  totalCombinations: total,
});

export const botId = '77777777-7777-4777-8777-777777777777';

export const botSummary = {
  id: botId,
  name: 'BTC EMA час',
  strategyId: emaSummary.id,
  category: 'linear',
  symbol: 'BTCUSDT',
  interval: '60',
  balance: '1000',
  lastBacktest: null,
  exchangeAccount: null,
  status: 'stopped',
  statusReason: null,
  closeRequested: false,
};

export const botDetails = {
  ...botSummary,
  parameterValues: { fast: 10 },
  createdAt: '2026-11-01T10:00:00Z',
  updatedAt: '2026-11-01T10:00:00Z',
};

export function backtestSummary(overrides: object = {}) {
  return {
    id: '88888888-8888-4888-8888-888888888888',
    strategyVersion: 1,
    from: '2024-01-01T00:00:00Z',
    to: '2024-07-01T00:00:00Z',
    parameters: {},
    status: 'Succeeded',
    error: null,
    progress: progressOf(1, 1),
    createdAt: '2026-11-02T14:05:00Z',
    botChanged: false,
    ...overrides,
  };
}

export const backtestId = '88888888-8888-4888-8888-888888888888';
export const otherBacktestId = '99999999-9999-4999-8999-999999999999';

/** A backtest as `GET /api/backtests/{id}` answers: running 5 of 9 combinations of two ranges. */
export function backtestFull(overrides: object = {}) {
  return {
    id: backtestId,
    botId,
    strategyId: emaSummary.id,
    strategyVersion: 7,
    engineVersion: 3,
    category: 'linear',
    symbol: 'BTCUSDT',
    interval: '60',
    from: '2024-01-01T00:00:00Z',
    to: '2024-07-01T00:00:00Z',
    parameters: {
      fast: { min: 5, max: 15, step: 5 },
      slow: { min: 20, max: 40, step: 10 },
      takeProfit: 3.5,
    },
    searchMode: 'grid',
    simulation: {
      balance: '1000',
      limitFeePercent: '0.02',
      marketFeePercent: '0.055',
      slippagePercent: '0',
      limitFillMode: 'breakout',
    },
    rules: { tickSize: '0.1', quantityStep: '0.001', minQuantity: '0.001', minAmount: '5' },
    status: 'Running',
    error: null,
    progress: progressOf(5, 9),
    speed: '412000',
    actualStart: '2024-01-01T00:00:00Z',
    gaps: [],
    createdAt: '2026-11-02T14:05:00Z',
    startedAt: '2026-11-02T14:05:01Z',
    finishedAt: null,
    botChanged: false,
    ...overrides,
  };
}

export function metricsOf(overrides: object = {}) {
  return {
    startEquity: '1000',
    endEquity: '1184.2',
    pnl: '184.20',
    realizedPnl: '160.10',
    unrealizedPnl: '24.10',
    return: '18.42',
    maxDrawdown: '6.10',
    returnToDrawdown: '3.02',
    closedTrades: 41,
    winRate: '58.5',
    fills: 82,
    fees: '21.70',
    maxPositionCost: '980',
    maxPositionCostPercent: '98',
    maxCapitalUsed: '1000',
    timeInPosition: '62',
    timeUnderWater: '18',
    longestUnderWater: 96,
    skippedOrders: 3,
    candles: 4344,
    ...overrides,
  };
}

/** A result of the grid `fast` 5, 10, 15 by `slow` 20, 30, 40, in the order of its combination number. */
export function resultOf(combination: number, metrics: object = {}) {
  return {
    combination,
    parameterValues: {
      fast: [5, 10, 15][Math.floor(combination / 3)],
      slow: [20, 30, 40][combination % 3],
      takeProfit: 3.5,
    },
    metrics: metricsOf(metrics),
    calculationTime: '00:00:00.1200000',
  };
}

export const exchangeAccount = {
  id: '99999999-9999-4999-8999-999999999999',
  name: 'Проверка',
  apiKey: 'XKz8apikeyMAIN',
  uid: 24617703,
  categories: ['spot', 'linear'],
  createdAt: '2026-11-02T10:00:00Z',
};

/** A row of the market snapshot with every metric empty; a test sets the ones it needs. */
export const marketRow = (overrides: Partial<Schemas['MarketRowResponse']> = {}) =>
  ({
    category: 'linear',
    symbol: 'BTCUSDT',
    price: '67000',
    change5m: null,
    change15m: null,
    change1h: null,
    change4h: null,
    change24h: null,
    turnover1h: null,
    turnover24h: null,
    openInterest: null,
    openInterestChange1h: null,
    openInterestChange24h: null,
    fundingRate: null,
    spread: null,
    incomplete: false,
    computedAt: new Date().toISOString(),
    setups: { '5': [], '15': [], '60': [], '240': [] },
    ...overrides,
  }) satisfies Schemas['MarketRowResponse'];

/**
 * The answer of `GET /api/market`: the page of the rows given as the first page of 50, a complete history and no pause unless a
 * test says otherwise. The counts are those of the rows: the pairs of each category and, on 15 minutes, of each setup.
 */
export const marketSnapshot = (
  rows: Schemas['MarketRowResponse'][],
  overrides: Partial<Schemas['MarketResponse']> = {},
) =>
  ({
    snapshotAt: new Date().toISOString(),
    history: { loaded: rows.length, total: rows.length },
    restPausedUntil: null,
    items: rows,
    page: 1,
    pageSize: 50,
    total: rows.length,
    pairs: rows.length,
    categories: {
      linear: rows.filter((row) => row.category === 'linear').length,
      spot: rows.filter((row) => row.category === 'spot').length,
    },
    setups: {
      all: rows.length,
      ...Object.fromEntries(
        [
          'impulse',
          'early_impulse',
          'drop',
          'early_drop',
          'high_volatility',
          'breakout',
          'false_breakout',
          'squeeze',
          'exhaustion',
          'rsi_extreme',
          'new_extreme',
          'quiet_volume',
        ].map((setup) => [setup, rows.filter((row) => row.setups['15']?.includes(setup)).length]),
      ),
    },
    ...overrides,
  }) satisfies Schemas['MarketResponse'];

/** The answer of `GET /api/ideas`: the ideas given as the first page of 50, all that there are. */
export const ideasPage = (
  items: Schemas['IdeaResponse'][],
  overrides: Partial<Schemas['IdeasPageResponse']> = {},
) =>
  ({
    items,
    page: 1,
    pageSize: 50,
    total: items.length,
    ...overrides,
  }) satisfies Schemas['IdeasPageResponse'];

/** A trade idea of the API: a long «Откат» on 15 minutes, awaiting entry, with a bucket that has enough data. */
export const ideaOf = (overrides: Partial<Schemas['IdeaResponse']> = {}) =>
  ({
    id: '11111111-1111-4111-8111-111111111111',
    category: 'linear',
    symbol: 'WIFUSDT',
    setup: 'impulse',
    interval: '15',
    type: 'pullback',
    side: 'long',
    firedAt: '2026-11-04T11:45:00Z',
    entry: '2.40',
    stop: '2.30',
    target: '2.60',
    riskReward: '2',
    netRiskReward: '1.84345',
    tick: null,
    profitPerUsdt: '0.038878',
    lossPerUsdt: '0.021089',
    riskPerUsdt: '0.02',
    status: 'awaiting_entry',
    statusAt: '2026-11-04T11:46:00Z',
    enteredAt: null,
    exitPrice: null,
    move: '3.1',
    volumeRatio: '4.2',
    breakEven: '0.3517',
    bucket: {
      cases: 120,
      targets: 55,
      stops: 61,
      expired: 4,
      probability: '0.46',
      probabilityLow: '0.41',
      probabilityHigh: '0.51',
      expectancy: '0.38',
      expectancyLow: '0.12',
      expectancyHigh: '0.64',
      badge: 'up',
      enoughData: true,
      signals: null,
      entryShare: null,
      signalExpectancy: null,
      ownProbability: '0.48',
      ownProbabilityLow: '0.39',
      ownProbabilityHigh: '0.57',
      ownExpectancy: '0.45',
      ownExpectancyLow: '0.1',
      ownExpectancyHigh: '0.8',
      parentWeight: '0.3',
      fewOwnCases: false,
    },
    recent: null,
    ...overrides,
  }) satisfies Schemas['IdeaResponse'];

export const savedFilterOf = (overrides: Partial<Schemas['SavedFilterResponse']> = {}) =>
  ({
    id: '22222222-2222-4222-8222-222222222222',
    name: 'Шорт перегретых',
    place: 'market',
    conditions: [
      { field: 'change1h', operator: '>', value: '3', to: null },
      { field: 'fundingRate', operator: '>', value: '0.0003', to: null },
    ],
    ...overrides,
  }) satisfies Schemas['SavedFilterResponse'];

const comparisons = ['>', '>=', '<', '<=', '=', 'between'];

/** The catalog of the fields of saved filters, as the API gives it, in its order. */
export const filterFields = [
  {
    code: 'category',
    unit: 'choice',
    ofIdea: false,
    operators: ['='],
    choices: ['spot', 'linear'],
  },
  ...['change5m', 'change15m', 'change1h', 'change4h', 'change24h'].map((code) => ({
    code,
    unit: 'percent',
    ofIdea: false,
    operators: comparisons,
    choices: null,
  })),
  ...['turnover1h', 'turnover24h', 'openInterest'].map((code) => ({
    code,
    unit: 'usdt',
    ofIdea: false,
    operators: comparisons,
    choices: null,
  })),
  ...['openInterestChange1h', 'openInterestChange24h'].map((code) => ({
    code,
    unit: 'percent',
    ofIdea: false,
    operators: comparisons,
    choices: null,
  })),
  { code: 'fundingRate', unit: 'ratio', ofIdea: false, operators: comparisons, choices: null },
  { code: 'spread', unit: 'percent', ofIdea: false, operators: comparisons, choices: null },
  {
    code: 'setup',
    unit: 'choice',
    ofIdea: false,
    operators: ['='],
    choices: [
      'impulse',
      'early_impulse',
      'drop',
      'early_drop',
      'high_volatility',
      'breakout',
      'false_breakout',
      'squeeze',
      'exhaustion',
      'rsi_extreme',
      'new_extreme',
      'quiet_volume',
    ],
  },
  {
    code: 'ideaSetup',
    unit: 'choice',
    ofIdea: true,
    operators: ['='],
    choices: [
      'impulse',
      'early_impulse',
      'drop',
      'early_drop',
      'high_volatility',
      'breakout',
      'false_breakout',
      'squeeze',
      'exhaustion',
      'rsi_extreme',
      'new_extreme',
      'quiet_volume',
    ],
  },
  {
    code: 'type',
    unit: 'choice',
    ofIdea: true,
    operators: ['='],
    choices: ['levels', 'target_2r', 'beyond_extreme', 'pullback', 'stop_entry'],
  },
  {
    code: 'interval',
    unit: 'choice',
    ofIdea: true,
    operators: ['='],
    choices: ['5', '15', '60', '240'],
  },
  { code: 'side', unit: 'choice', ofIdea: true, operators: ['='], choices: ['long', 'short'] },
  { code: 'riskReward', unit: 'ratio', ofIdea: true, operators: comparisons, choices: null },
  { code: 'probability', unit: 'ratio', ofIdea: true, operators: comparisons, choices: null },
  {
    code: 'expectancy',
    unit: 'r',
    ofIdea: true,
    operators: [...comparisons, 'reliably_positive'],
    choices: null,
  },
] satisfies Schemas['SavedFilterFieldResponse'][];

/** A trade by an idea waiting for its entry on SOLUSDT; a test sets what it needs. */
export const tradeOf = (overrides: Partial<Schemas['IdeaTradeResponse']> = {}) =>
  ({
    id: 'aaaaaaaa-0000-4000-8000-000000000001',
    ideaId: '11111111-1111-4111-8111-111111111111',
    accountId: exchangeAccount.id,
    accountName: exchangeAccount.name,
    category: 'linear',
    symbol: 'SOLUSDT',
    setup: 'impulse',
    interval: '15',
    type: 'target_2r',
    side: 'long',
    entryPrice: '168.4',
    filledPrice: null,
    stop: '165',
    target: '175',
    quantity: '0.47',
    amount: '79.15',
    risk: '0.99',
    status: 'awaiting_entry',
    statusAt: '2026-11-05T10:00:00Z',
    createdAt: '2026-11-05T10:00:00Z',
    reason: null,
    result: null,
    resultR: null,
    openedAt: null,
    secondsToOutcome: null,
    promise: null,
    ideaOutcome: null,
    lastPrice: null,
    currentResult: null,
    currentResultR: null,
    origin: 'manual',
    autoId: null,
    autoName: null,
    ...overrides,
  }) satisfies Schemas['IdeaTradeResponse'];

/** The totals of a journal with nothing closed; a test sets what it needs. */
export const journalTotals = (overrides: Partial<Schemas['IdeaJournalTotalsResponse']> = {}) =>
  ({
    targets: 0,
    stops: 0,
    manual: 0,
    cancelled: 0,
    failed: 0,
    result: '0',
    fees: '0',
    meanR: null,
    meanRLow: null,
    meanRHigh: null,
    meanSecondsToOutcome: null,
    promise: { count: 0, promised: null, got: null },
    ideas: { count: 0, ideas: null, trades: null },
    ...overrides,
  }) satisfies Schemas['IdeaJournalTotalsResponse'];

/** The journal as the API gives it: the open trades, a page of the closed ones and the totals. */
export const journalOf = (
  open: Schemas['IdeaTradeResponse'][] = [],
  closed: Schemas['IdeaTradeResponse'][] = [],
  overrides: {
    totals?: Schemas['IdeaJournalTotalsResponse'];
    paperTotals?: Schemas['IdeaJournalTotalsResponse'];
    page?: number;
    pageSize?: number;
    total?: number;
  } = {},
) =>
  ({
    open,
    closed: {
      items: closed,
      page: overrides.page ?? 1,
      pageSize: overrides.pageSize ?? 50,
      total: overrides.total ?? closed.length,
    },
    totals: overrides.totals ?? journalTotals(),
    paperTotals: overrides.paperTotals ?? journalTotals(),
  }) satisfies Schemas['IdeaJournalResponse'];

export const tradingSettings = {
  accountId: exchangeAccount.id,
  amount: '20',
  maxAmount: '200',
  maxOpenTrades: 5,
  maxTotalRisk: '5',
  minRiskReward: '1.5',
  dailyLossLimit: null,
  maxSameDirection: null,
  autosBudget: null,
} satisfies Schemas['IdeaTradingSettingsResponse'];

/** The entry of an idea as the API calculates it: no reason. */
export const entryOf = (overrides: Partial<Schemas['IdeaEntryResponse']> = {}) =>
  ({
    accountId: exchangeAccount.id,
    entryPrice: '168.40',
    quantity: '0.47',
    amount: '79.15',
    risk: '0.99',
    profit: '1.82',
    riskReward: '1.84',
    riskRewardWithoutFees: '2',
    freeMoney: '1000',
    quote: 'USDT',
    reasons: [],
    ...overrides,
  }) satisfies Schemas['IdeaEntryResponse'];

/** A row of a table of calibration of the API: 640 cases promised 35 % on average. */
export const calibrationRowOf = (overrides: Partial<Schemas['CalibrationRowResponse']> = {}) =>
  ({
    from: '0.3',
    to: '0.4',
    badge: null,
    promised: '0.35',
    cases: 640,
    targetShare: '0.34',
    stopShare: '0.52',
    expiredShare: '0.14',
    probabilityLow: '0.31',
    probabilityHigh: '0.37',
    expectancy: '0.02',
    expectancyLow: '-0.05',
    expectancyHigh: '0.09',
    ...overrides,
  }) satisfies Schemas['CalibrationRowResponse'];

/** A report of calibration of the API over 90 days and all categories, the numbers of the board «Идеи — калибровка». */
export const calibrationOf = (overrides: Partial<Schemas['IdeaCalibrationResponse']> = {}) =>
  ({
    computedAt: '2026-11-06T00:50:00Z',
    days: 90,
    category: null,
    checked: 2412,
    unchecked: 318,
    brier: '0.207',
    baseBrier: '0.219',
    probabilities: [
      calibrationRowOf(),
      calibrationRowOf({
        from: '0.6',
        to: null,
        promised: '0.64',
        cases: 296,
        targetShare: '0.51',
        stopShare: '0.38',
        expiredShare: '0.11',
        probabilityLow: '0.45',
        probabilityHigh: '0.57',
      }),
    ],
    expectancies: [
      calibrationRowOf({ from: '0', to: '0.2', promised: '0.11', cases: 900, expectancy: '0.04' }),
    ],
    badges: [
      calibrationRowOf({
        from: null,
        to: null,
        badge: 'up',
        promised: '0.21',
        cases: 603,
        targetShare: '0.46',
        expectancy: '0.08',
        expectancyLow: '0.02',
        expectancyHigh: '0.14',
      }),
    ],
    total: calibrationRowOf({
      from: null,
      to: null,
      promised: '0.12',
      cases: 2412,
      expectancy: '0.03',
    }),
    scope: 'all',
    owns: [
      {
        fewOwnCases: false,
        brier: '0.205',
        promised: '0.13',
        cases: 1800,
        targetShare: '0.41',
        expectancy: '0.04',
        expectancyLow: '-0.02',
        expectancyHigh: '0.1',
      },
      {
        fewOwnCases: true,
        brier: '0.212',
        promised: '0.09',
        cases: 612,
        targetShare: '0.38',
        expectancy: '0.01',
        expectancyLow: '-0.07',
        expectancyHigh: '0.09',
      },
    ],
    comparison: {
      old: {
        cases: 1800,
        brier: '0.231',
        up: calibrationRowOf({
          from: null,
          to: null,
          badge: 'up',
          promised: '0.145',
          cases: 1200,
          expectancy: '-0.074',
          expectancyLow: '-0.12',
          expectancyHigh: '-0.03',
        }),
      },
      new: {
        cases: 1800,
        brier: '0.204',
        up: calibrationRowOf({
          from: null,
          to: null,
          badge: 'up',
          promised: '0.05',
          cases: 300,
          expectancy: '0.03',
          expectancyLow: '-0.04',
          expectancyHigh: '0.1',
        }),
      },
    },
    intervals: [
      {
        interval: '60',
        type: null,
        total: calibrationRowOf({
          from: null,
          to: null,
          promised: '0.02',
          cases: 4000,
          expectancy: '-0.05',
          expectancyLow: '-0.09',
          expectancyHigh: '-0.01',
        }),
        up: calibrationRowOf({
          from: null,
          to: null,
          badge: 'up',
          promised: '0.15',
          cases: 900,
          expectancy: '-0.1',
          expectancyLow: '-0.18',
          expectancyHigh: '-0.02',
        }),
        comparison: {
          old: {
            cases: 1500,
            brier: '0.233',
            up: calibrationRowOf({
              from: null,
              to: null,
              badge: 'up',
              promised: '0.16',
              cases: 1000,
              expectancy: '-0.12',
              expectancyLow: '-0.2',
              expectancyHigh: '-0.04',
            }),
          },
          new: { cases: 1500, brier: '0.21', up: null },
        },
      },
    ],
    types: [
      {
        interval: null,
        type: 'pullback',
        total: calibrationRowOf({
          from: null,
          to: null,
          promised: '0.01',
          cases: 500,
          expectancy: '0.02',
          expectancyLow: null,
          expectancyHigh: null,
        }),
        up: null,
        comparison: {
          old: {
            cases: 200,
            brier: '0.24',
            up: calibrationRowOf({
              from: null,
              to: null,
              badge: 'up',
              promised: '0.1',
              cases: 120,
              expectancy: '-0.08',
              expectancyLow: '-0.16',
              expectancyHigh: '0',
            }),
          },
          new: {
            cases: 200,
            brier: '0.22',
            up: calibrationRowOf({
              from: null,
              to: null,
              badge: 'up',
              promised: '0.04',
              cases: 40,
              expectancy: '0.01',
              expectancyLow: '-0.1',
              expectancyHigh: '0.12',
            }),
          },
        },
      },
    ],
    ...overrides,
  }) satisfies Schemas['IdeaCalibrationResponse'];

const exitRuleTypes = ['levels', 'target_2r', 'beyond_extreme', 'pullback', 'stop_entry'];
const exitRuleIntervals = ['5', '15', '60', '240'];

/** The default exit rule of a type × interval, as the API gives it. */
export const exitRuleOf = (overrides: Partial<Schemas['IdeaExitRuleResponse']> = {}) =>
  ({
    type: 'pullback',
    interval: '15',
    life: 24,
    target: '2',
    window: 4,
    appliedAt: null,
    ...overrides,
  }) satisfies Schemas['IdeaExitRuleResponse'];

/** The rules of every type × interval at their defaults, with the ones given in place of theirs, and the grids. */
export const exitRulesOf = (applied: Schemas['IdeaExitRuleResponse'][] = []) =>
  ({
    rules: exitRuleTypes.flatMap((type) =>
      exitRuleIntervals.map(
        (interval) =>
          applied.find((rule) => rule.type === type && rule.interval === interval) ??
          exitRuleOf({
            type,
            interval,
            target: type === 'levels' ? null : '2',
            window: type === 'pullback' ? 4 : type === 'stop_entry' ? 12 : null,
          }),
      ),
    ),
    grids: [
      {
        interval: '5',
        candles: [
          1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384, 512, 768, 1024, 1536,
          2016,
        ],
      },
      {
        interval: '15',
        candles: [1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384, 512, 672],
      },
      { interval: '60', candles: [1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 128, 168] },
      { interval: '240', candles: [1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 42] },
    ],
  }) satisfies Schemas['IdeaExitRulesResponse'];

export const exitBucketOf = (overrides: Partial<Schemas['BucketStatisticsResponse']> = {}) =>
  ({
    cases: 100,
    targets: 30,
    stops: 60,
    expired: 10,
    probability: '0.3',
    probabilityLow: '0.22',
    probabilityHigh: '0.4',
    expectancy: '-0.06',
    expectancyLow: '-0.2',
    expectancyHigh: '0.08',
    badge: 'flat',
    enoughData: true,
    signals: 200,
    entryShare: '0.5',
    signalExpectancy: '-0.03',
    ownProbability: null,
    ownProbabilityLow: null,
    ownProbabilityHigh: null,
    ownExpectancy: '0',
    ownExpectancyLow: null,
    ownExpectancyHigh: null,
    parentWeight: null,
    fewOwnCases: false,
    ...overrides,
  }) satisfies Schemas['BucketStatisticsResponse'];

/** A preview of «Откат» 15 minutes: −0.06 → −0.02 R over 90 days, −0.09 → −0.08 R over 30, +2.5 R reached by 29 % at 30 % needed. */
export const exitPreviewOf = (overrides: Partial<Schemas['ExitRulePreviewResponse']> = {}) =>
  ({
    last90: {
      current: exitBucketOf(),
      candidate: exitBucketOf({ expectancy: '-0.02', signalExpectancy: '-0.01' }),
    },
    last30: {
      current: exitBucketOf({ cases: 40, signals: 80, expectancy: '-0.09' }),
      candidate: exitBucketOf({ cases: 40, signals: 80, expectancy: '-0.08' }),
    },
    lives: [1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384, 512, 672].map(
      (candles) => ({
        candles,
        expectancy: '-0.05',
        signalExpectancy: '-0.025',
      }),
    ),
    mfe: ['0.5', '1', '1.5', '2', '2.5', '3', '3.5', '4', '4.5', '5'].map((r) => ({
      r,
      reached: r === '2.5' ? '0.29' : '0.4',
      breakEven: r === '2.5' ? '0.2957' : '0.33',
    })),
    ...overrides,
  }) satisfies Schemas['ExitRulePreviewResponse'];

/**
 * An out-of-sample check of «Откат» 15 minutes 24 · 2R · 4: the candidate +0.05 → −0.03 R, the best of the selection 96 · 3R +0.12 R on 60
 * entries → −0.01 R on 12, the overfitting 13 of 20 splits.
 */
export const exitOutOfSampleOf = (
  overrides: Partial<Schemas['ExitRuleOutOfSampleResponse']> = {},
) =>
  ({
    selectionFrom: '2026-08-08T12:00:00Z',
    checkFrom: '2026-10-07T12:00:00Z',
    to: '2026-10-30T11:59:00Z',
    rules: 171,
    candidate: {
      selectionTo: '2026-10-07T05:59:00Z',
      selection: exitBucketOf({
        cases: 60,
        expectancy: '0.05',
        expectancyLow: '-0.02',
        expectancyHigh: '0.12',
      }),
      check: exitBucketOf({
        cases: 40,
        expectancy: '-0.03',
        expectancyLow: '-0.1',
        expectancyHigh: '0.04',
      }),
    },
    best: {
      life: 96,
      target: '3',
      selectionTo: '2026-10-06T11:59:00Z',
      selection: exitBucketOf({
        cases: 60,
        expectancy: '0.12',
        expectancyLow: '0.02',
        expectancyHigh: '0.22',
        badge: 'up',
      }),
      check: exitBucketOf({
        cases: 12,
        expectancy: '-0.01',
        expectancyLow: null,
        expectancyHigh: null,
        badge: null,
      }),
    },
    overfitting: { splits: 20, overfit: 13, probability: '0.65' },
    ...overrides,
  }) satisfies Schemas['ExitRuleOutOfSampleResponse'];

/** An auto of the list as the API gives it: «Импульс лонг» with money on the account of the tests; a test sets what it needs. */
export const autoOf = (overrides: Partial<Schemas['IdeaAutoResponse']> = {}) =>
  ({
    id: 'bbbbbbbb-0000-4000-8000-000000000001',
    name: 'Импульс лонг',
    accountId: exchangeAccount.id,
    accountName: exchangeAccount.name,
    amount: '300',
    mode: 'live',
    setups: ['impulse', 'early_impulse'],
    types: ['pullback', 'target_2r'],
    intervals: ['15', '60'],
    side: 'long',
    category: 'linear',
    badge: 'up',
    minRiskReward: '1.5',
    minCases: 100,
    position: 0,
    budget: null,
    ...overrides,
  }) satisfies Schemas['IdeaAutoResponse'];

/** An auto of the list with its totals: nothing closed, no refusals, nothing open. */
export const autoItemOf = (
  auto: Schemas['IdeaAutoResponse'] = autoOf(),
  overrides: Partial<Omit<Schemas['IdeaAutoItemResponse'], 'auto'>> = {},
) =>
  ({
    auto,
    paperClosed: 0,
    paperMeanR: null,
    paperSumR: '0',
    realClosed: 0,
    realResult: '0',
    refusalsToday: 0,
    budgetUsed: '0',
    ...overrides,
  }) satisfies Schemas['IdeaAutoItemResponse'];

/** The guards of the money with room to spare and none of them off; a test sets what it needs. The trades of the autos take 180 of 200. */
export const guardsOf = (overrides: Partial<Schemas['IdeaGuardsResponse']> = {}) =>
  ({
    openTrades: 3,
    maxOpenTrades: 8,
    openRisk: '10.22',
    maxTotalRisk: '40',
    daySum: '-19.05',
    dailyLossLimit: '30',
    triggeredAt: null,
    resumesAt: null,
    openLongs: 2,
    openShorts: 1,
    maxSameDirection: 5,
    autosBudgetUsed: '180',
    autosBudget: '200',
    ...overrides,
  }) satisfies Schemas['IdeaGuardsResponse'];

/** A refusal of an auto as the API gives it: the pair taken by another auto above. */
export const refusalOf = (overrides: Partial<Schemas['IdeaAutoRefusalResponse']> = {}) =>
  ({
    id: 'cccccccc-0000-4000-8000-000000000001',
    at: '2026-11-07T09:30:00Z',
    autoId: 'bbbbbbbb-0000-4000-8000-000000000001',
    autoName: 'Падения шорт',
    mode: 'live',
    category: 'linear',
    symbol: 'SUIUSDT',
    setup: 'drop',
    interval: '60',
    type: 'target_2r',
    side: 'short',
    reasons: ['pair_taken'],
    detail: {
      actual: null,
      limit: null,
      holder: {
        kind: 'other_auto',
        autoName: 'Импульс лонг',
        above: true,
        paper: false,
        accountName: 'Bybit · ideas',
        since: '2026-11-07T08:00:00Z',
      },
    },
    ...overrides,
  }) satisfies Schemas['IdeaAutoRefusalResponse'];

/** A page of refusals. */
export const refusalsPage = (items: Schemas['IdeaAutoRefusalResponse'][] = []) => ({
  items,
  page: 1,
  pageSize: 50,
  total: items.length,
});
