import type { RiskSettings, RiskState } from '@/api/risk';
import type { EntryPreview, Trade, TradePage } from '@/api/trades';

import { type FakeRoute, json } from './fakeApi';

/** An open trade of the tests: a long `SOLUSDT` of 100 USDT at 3×, +1.18 USDT now; tests run in UTC+3. */
export function trade(overrides: Partial<Trade> = {}): Trade {
  return {
    id: 'trade-sol',
    ideaId: 'idea-sol',
    symbol: 'SOLUSDT',
    side: 'long',
    strategy: 'breakout-long',
    version: 7,
    timeframe: '1h',
    ideaMoment: Date.UTC(2026, 10, 28, 11),
    until: Date.now() + (2 * 60 + 12) * 60_000 + 30_000,
    ideaEntry: 236.1,
    accountId: 'a1',
    accountName: 'Основной',
    amount: 100,
    leverage: 3,
    clickPrice: 237.4,
    quantity: 0.4,
    held: 31.65,
    stop: 231.38,
    stopSource: 'idea',
    target: 245.54,
    state: 'open',
    reason: null,
    closeReason: null,
    entryPrice: 237.42,
    exitPrice: null,
    fees: 0,
    funding: 0,
    result: null,
    resultShare: null,
    unrealised: 1.18,
    markedAt: Date.now(),
    createdAt: Date.UTC(2026, 10, 28, 11, 1),
    openedAt: Date.UTC(2026, 10, 28, 11, 1),
    closedAt: null,
    ideaResult: null,
    ...overrides,
  };
}

/** A page of trades; the counts of the tabs are those of the rows unless given. */
export function tradePage(
  items: readonly Trade[],
  counts: { open?: number; closed?: number } = {},
): FakeRoute {
  return () =>
    json({
      items: [...items],
      total: items.length,
      page: 1,
      pageSize: 50,
      open: counts.open ?? items.length,
      closed: counts.closed ?? 0,
    } satisfies TradePage);
}

/** The state of the risk module with limits set: −4.20 of −50, 2 of 5 trades, 300 of 1 000 USDT, a Worker and one stream. */
export function riskState(overrides: Partial<RiskState> = {}): RiskState {
  return {
    limitsSet: true,
    missing: [],
    dayLoss: 4.2,
    daySum: -4.2,
    dayLimit: 50,
    dayFrom: Date.UTC(2026, 10, 28),
    openTrades: 2,
    maxOpenTrades: 5,
    openAmount: 300,
    maxTotalAmount: 1000,
    halt: { haltedAt: null, loss: null, limit: null, notClosed: [], liftedAt: null },
    traderAlive: true,
    trader: 'worker-a',
    accounts: 1,
    streamed: 1,
    polled: [],
    ...overrides,
  };
}

/** The settings of the tests: 3×, 100 USDT, limits 200 / 5 / 1 000 / 3 % / 50, the defaults of time and attempts. */
export function riskSettings(overrides: Partial<RiskSettings> = {}): RiskSettings {
  return {
    defaultAccountId: 'a1',
    defaultAmount: 100,
    leverage: 3,
    maxAmount: 200,
    maxOpenTrades: 5,
    maxTotalAmount: 1000,
    maxLossPercent: 3,
    maxDayLoss: 50,
    expiryMarginMinutes: 1,
    entryWaitSeconds: 30,
    stopAttempts: 3,
    savedAt: Date.UTC(2026, 10, 28, 11, 20),
    ...overrides,
  };
}

/** The settings of a fresh bench: no limits, the defaults of time and attempts. */
export function emptySettings(): RiskSettings {
  return riskSettings({
    defaultAccountId: null,
    defaultAmount: null,
    leverage: null,
    maxAmount: null,
    maxOpenTrades: null,
    maxTotalAmount: null,
    maxLossPercent: null,
    maxDayLoss: null,
    savedAt: null,
  });
}

const passed = (code: string, text: string) => ({ code, passed: true, text });

/** The count of an entry of the long idea `SOLUSDT` at 237.40 for 100 USDT with every check passed. */
export function preview(overrides: Partial<EntryPreview> = {}): EntryPreview {
  return {
    side: 'long',
    price: 237.4,
    amount: 100,
    leverage: 3,
    quantity: 0.4,
    value: 94.96,
    margin: 31.65,
    ideaEntry: 236.1,
    ideaStop: 231.38,
    ideaTarget: 245.54,
    stop: 231.38,
    stopSource: 'idea',
    limitStop: 230.28,
    stopDistance: -0.0254,
    target: 245.54,
    targetDistance: 0.0343,
    lossAtStop: -2.51,
    profitAtTarget: 3.16,
    liquidation: 159.5,
    needed: 33.4,
    until: Date.UTC(2026, 10, 28, 15),
    accountId: 'a1',
    accountName: 'Основной',
    free: 1250.4,
    checks: [
      passed('amount', 'Сумма не больше 200 USDT'),
      passed('total', 'В сделках 300 из 1 000 USDT'),
      passed('position', 'По SOLUSDT сделок и позиций нет'),
      passed('margin', 'Аккаунт в режиме изолированной маржи'),
      passed('open', 'Открытых сделок 2 из 5'),
      passed('free', 'Свободных USDT хватает'),
      passed('liquidation', 'Стоп ближе ликвидации (≈ 159.5)'),
    ],
    passed: true,
    ...overrides,
  };
}
