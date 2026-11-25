import type { MarketPair } from '@/api/market';

import { type FakeRoute, json } from './fakeApi';

/** A pair of the snapshot with the numbers a test does not check. */
export function pair(symbol: string, overrides: Partial<MarketPair> = {}): MarketPair {
  return {
    symbol,
    kind: 'crypto',
    price: 100,
    change1h: 0.001,
    change4h: 0.002,
    change24h: 0.01,
    turnover24h: 1_000_000_000,
    openInterest: 1000,
    openInterestUsdt: 500_000_000,
    fundingRate: 0.0001,
    nextFunding: null,
    updatedAt: Date.UTC(2026, 10, 22, 14, 32, 5),
    ideas: [],
    ...overrides,
  };
}

/**
 * The list of the market of `total` pairs `P001USDT`, `P002USDT`… as the server pages it: the rows of the page asked,
 * in the order asked by their number.
 */
export function marketList(total: number): FakeRoute {
  return (request) => {
    const query = new URL(request.url).searchParams;
    const page = Number(query.get('page') ?? 1);
    const size = Number(query.get('pageSize') ?? 50);
    const numbers = Array.from({ length: total }, (_, index) => index + 1);
    const ordered = query.get('direction') === 'asc' ? numbers : numbers.reverse();
    const items = ordered
      .slice((page - 1) * size, page * size)
      .map((number) =>
        pair(`P${String(number).padStart(3, '0')}USDT`, { turnover24h: number * 1e6 }),
      );
    return json({
      items,
      total,
      page,
      pageSize: size,
      filterGone: false,
      updatedAt: Date.UTC(2026, 10, 22, 14, 32, 5),
    });
  };
}

/** The query of every request of the list of the market. */
export function marketQueries(requests: readonly Request[]): URLSearchParams[] {
  return requests
    .map((request) => new URL(request.url))
    .filter((url) => url.pathname === '/api/market/pairs')
    .map((url) => url.searchParams);
}
