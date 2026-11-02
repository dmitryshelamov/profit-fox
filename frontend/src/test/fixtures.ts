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

export const testnetAccount = {
  id: '99999999-9999-4999-8999-999999999999',
  name: 'Проверка',
  environment: 'testnet',
  apiKey: 'XKz8apikeyTESTNET',
  uid: 24617703,
  categories: ['spot', 'linear'],
  createdAt: '2026-11-02T10:00:00Z',
};
