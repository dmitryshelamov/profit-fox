import { keepPreviousData, queryOptions } from '@tanstack/react-query';

import { type CandlesPage, useCandlePages } from '@/modules/market-chart';
import { api, unwrap } from '@/shared/api/client';

import type { CategoryFilter } from './model/rows';
import type { SetupFilter } from './model/setups';
import type { Sort } from './model/sorting';

/** What a page of the market is asked for: the filters, the search, the setup and its interval, the saved filter, the order, and the number and the size of the page. */
export interface MarketRequest {
  readonly category: CategoryFilter;
  readonly search: string;
  readonly setup: SetupFilter;
  readonly setupInterval: string;
  readonly savedFilterId: string | null;
  readonly sort: Sort;
  readonly page: number;
  readonly pageSize: number;
}

/**
 * One page of the snapshot of the market with the counts of the screen and the state of the scanner, read again every 15
 * seconds; the API filters, sorts and cuts it. The old page stays on the screen while a new one loads.
 */
export function marketQuery(request: MarketRequest, enabled: boolean) {
  const { category, search, setup, setupInterval, savedFilterId, sort, page, pageSize } = request;

  return queryOptions({
    queryKey: ['market', 'page', request],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/market', {
          params: {
            query: {
              Category: category === 'all' ? undefined : category,
              Search: search === '' ? undefined : search,
              Setup: setup === 'all' ? undefined : setup,
              Interval: setupInterval,
              SavedFilter: savedFilterId ?? undefined,
              Sort: sort.column,
              Direction: sort.direction,
              Page: page,
              PageSize: pageSize,
            },
          },
        }),
      ),
    placeholderData: keepPreviousData,
    refetchInterval: 15_000,
    enabled,
  });
}

/** The row of a pair, read again every 15 seconds; a pair that is not in the snapshot answers 404. */
export function marketPairQuery(category: string, symbol: string) {
  return queryOptions({
    queryKey: ['market', category, symbol],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/market/{category}/{symbol}', {
          params: { path: { category, symbol } },
        }),
      ),
    refetchInterval: 15_000,
  });
}

/** The open interest of a linear pair in coins, from `from` to now. */
export function openInterestQuery(category: string, symbol: string, from: string) {
  return queryOptions({
    queryKey: ['market', category, symbol, 'open-interest', from],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/market/{category}/{symbol}/open-interest', {
          params: {
            path: { category, symbol },
            query: { From: from, To: new Date().toISOString() },
          },
        }),
      ),
  });
}

/** The funding settlements of a linear pair, from `from` to now. */
export function fundingQuery(category: string, symbol: string, from: string) {
  return queryOptions({
    queryKey: ['market', category, symbol, 'funding', from],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/market/{category}/{symbol}/funding', {
          params: {
            path: { category, symbol },
            query: { From: from, To: new Date().toISOString() },
          },
        }),
      ),
  });
}

async function pairCandles(
  category: string,
  symbol: string,
  interval: string,
  from: string,
  to: string,
): Promise<CandlesPage> {
  const page = unwrap(
    await api.GET('/api/market/{category}/{symbol}/candles', {
      params: { path: { category, symbol }, query: { From: from, To: to, Interval: interval } },
    }),
  );

  return { from, to, candles: page.candles, unavailable: page.unavailable };
}

/** The history of a pair's chart: see `useCandlePages`, over the candles endpoint of the market. */
export function usePairCandles(category: string, symbol: string, interval: string) {
  return useCandlePages(['market', category, symbol], interval, (from, to) =>
    pairCandles(category, symbol, interval, from, to),
  );
}
