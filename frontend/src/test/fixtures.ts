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
    baseline: '0.33',
    bucket: {
      cases: 120,
      expired: 4,
      probability: '0.46',
      probabilityLow: '0.41',
      probabilityHigh: '0.51',
      baseline: '0.33',
      expectancy: '0.38',
      enoughData: true,
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
  {
    code: 'probability',
    unit: 'ratio',
    ofIdea: true,
    operators: [
      ...comparisons.filter((operator) => operator !== 'between'),
      'between',
      'above_baseline',
    ],
    choices: null,
  },
  { code: 'expectancy', unit: 'r', ofIdea: true, operators: comparisons, choices: null },
] satisfies Schemas['SavedFilterFieldResponse'][];
