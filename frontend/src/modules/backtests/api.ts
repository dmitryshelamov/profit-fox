import { queryOptions } from '@tanstack/react-query';

import { api, type Schemas, unwrap } from '@/shared/api/client';

import { isFinished } from './model/status';

/** The backtests of a bot, newest first; polled every 2 seconds while one of them is unfinished. */
export function botBacktestsQuery(botId: string) {
  return queryOptions({
    queryKey: ['bots', botId, 'backtests'],
    queryFn: async () =>
      unwrap(await api.GET('/api/bots/{botId}/backtests', { params: { path: { botId } } })),
    refetchInterval: (query) =>
      query.state.data?.some((backtest) => !isFinished(backtest.status)) ? 2000 : false,
    refetchIntervalInBackground: false,
  });
}

/** One backtest; polled every 2 seconds until it is finished. */
export function backtestQuery(id: string) {
  return queryOptions({
    queryKey: ['backtests', id],
    queryFn: async () => unwrap(await api.GET('/api/backtests/{id}', { params: { path: { id } } })),
    refetchInterval: (query) =>
      query.state.data && isFinished(query.state.data.status) ? false : 2000,
    refetchIntervalInBackground: false,
  });
}

export interface ResultsPage {
  /** A metric code, or `combination`. */
  readonly sort: string;
  readonly order: 'asc' | 'desc';
  readonly offset: number;
  readonly limit: number;
}

/** A page of results, sorted on the server; refreshed every 2 seconds while the backtest is unfinished. */
export function backtestResultsQuery(id: string, page: ResultsPage, polled = false) {
  return queryOptions({
    queryKey: ['backtests', id, 'results', page],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/backtests/{id}/results', {
          params: {
            path: { id },
            query: { Sort: page.sort, Order: page.order, Offset: page.offset, Limit: page.limit },
          },
        }),
      ),
    refetchInterval: polled ? 2000 : false,
    refetchIntervalInBackground: false,
  });
}

/** One result by its combination number; the saved numbers have gaps, so a position in the list is not a number. */
export function backtestResultQuery(id: string, combination: number) {
  return queryOptions({
    queryKey: ['backtests', id, 'result', combination],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/backtests/{id}/results/{combination}', {
          params: { path: { id, combination } },
        }),
      ),
    retry: false,
  });
}

/** The details of a result: fills, trades and the equity curve. */
export function resultDetailsQuery(id: string, combination: number) {
  return queryOptions({
    queryKey: ['backtests', id, 'details', combination],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/backtests/{id}/results/{combination}/details', {
          params: { path: { id, combination } },
        }),
      ),
    retry: false,
  });
}

export async function createBacktest(botId: string, body: Schemas['CreateBacktestBody']) {
  return unwrap(
    await api.POST('/api/bots/{botId}/backtests', { params: { path: { botId } }, body }),
  ).id;
}

export async function cancelBacktest(id: string): Promise<void> {
  unwrap(await api.POST('/api/backtests/{id}/cancel', { params: { path: { id } } }));
}
