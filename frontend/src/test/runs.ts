import type { Run, SetNumbers } from '@/api/runs';

/** The numbers of a set or of a pair; the fields not given are those of a set without orders. */
export function numbersOf(numbers: Partial<SetNumbers> = {}): SetNumbers {
  return {
    orders: 0,
    trades: 0,
    pairsWithTrades: 0,
    mean: null,
    amount: 0,
    winShare: null,
    excluded: { plan: 0, coverage: 0, funding: 0, noTrade: 0, period: 0 },
    skipped: 0,
    openCycles: 0,
    openAmount: 0,
    difference: null,
    ...numbers,
  };
}

/** A run of `funding-short` version 3 of ideas, on `4h` and every pair, as `GET /api/backtests/{id}` gives it. */
export function backtest(run: Partial<Run> & { readonly id: string }): Run {
  return {
    strategy: {
      id: '7a1c0f3e-2b4d-4e6f-8a9b-0c1d2e3f4a5b',
      name: 'funding-short',
      version: 3,
      kind: 'idea',
    },
    settings: {
      values: [{ name: 'threshold', values: ['0.0005'] }],
      timeframes: ['4h'],
      start: '2023-01-01',
      end: '2026-06-01',
      pairs: null,
      takerFee: 0.00055,
      makerFee: 0.0002,
    },
    state: 'queued',
    queuedAt: '2026-11-21T00:10:00Z',
    startedAt: null,
    finishedAt: null,
    worker: null,
    pairsDone: 0,
    pairsTotal: null,
    setCount: 1,
    sets: [
      {
        number: 1,
        timeframe: '4h',
        values: [{ name: 'threshold', value: '0.0005' }],
        numbers: null,
      },
    ],
    warning: 'Выжившие пары: стенд видит только пары, что торгуются сейчас.',
    ...run,
  };
}
