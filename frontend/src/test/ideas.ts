import type { Idea, ScannerState } from '@/api/ideas';

import { type FakeRoute, json } from './fakeApi';

/** An idea of the tests: `breakout-long` v7 long on `SOLUSDT` 1h, opened at 13:00 of 2026-11-22, two hours to go. */
export function idea(overrides: Partial<Idea> = {}): Idea {
  return {
    id: 'idea-sol',
    symbol: 'SOLUSDT',
    strategyId: 'strategy-breakout',
    strategy: 'breakout-long',
    version: 7,
    timeframe: '1h',
    side: 'long',
    labels: [],
    moment: Date.UTC(2026, 10, 22, 13),
    until: Date.now() + (2 * 60 + 28) * 60_000 + 30_000,
    size: 100,
    entry: 236.1,
    entryTime: Date.UTC(2026, 10, 22, 13, 1),
    stopFraction: 0.02,
    targetFraction: 0.04,
    stop: 231.38,
    target: 245.54,
    now: 0.0098,
    state: 'open',
    exitTime: null,
    exitReason: null,
    ambiguous: false,
    result: null,
    resultUsdt: null,
    excluded: null,
    ...overrides,
  };
}

/** The state of a scanner that keeps the live data with every pair warmed. */
export function scannerState(overrides: Partial<ScannerState> = {}): ScannerState {
  return {
    worker: 'worker-a',
    live: true,
    idleSeconds: 0,
    liveSince: Date.UTC(2026, 10, 22, 9, 15),
    warmed: 831,
    pairs: 831,
    open: 1,
    closed: 0,
    burst: { '1h': 8 },
    ...overrides,
  };
}

/** A page of ideas with every idea given. */
export function ideaPage(items: readonly Idea[]): FakeRoute {
  return () => json({ items, total: items.length, page: 1, pageSize: 50, filterGone: false });
}
