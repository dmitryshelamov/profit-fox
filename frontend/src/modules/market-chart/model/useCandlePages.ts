import {
  infiniteQueryOptions,
  queryOptions,
  useInfiniteQuery,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, useRef } from 'react';

import type { Schemas } from '@/shared/api/client';

import { candleEnd, intervalSeconds, mergeCandles, pageBefore, tooFarBehind } from './chartPaging';

/** A page of closed candles with the period that was asked for: the response does not repeat it. */
export interface CandlesPage {
  readonly from: string;
  readonly to: string;
  readonly candles: Schemas['CandleResponse'][];
  readonly unavailable: boolean;
}

/** Reads the closed candles of `[from, to)`: the endpoint of a bot or of a pair. */
export type ReadCandles = (from: string, to: string) => Promise<CandlesPage>;

const candlesKey = (scope: readonly unknown[], interval: string) =>
  [...scope, 'candles', interval] as const;

/**
 * The closed candles of an instrument, a page of 500 going back from now at a time. Paging stops at a page without
 * candles: the history starts there or cannot be read.
 */
function candlesQuery(scope: readonly unknown[], interval: string, read: ReadCandles) {
  return infiniteQueryOptions({
    queryKey: candlesKey(scope, interval),
    queryFn: async ({ pageParam }) => {
      const period = pageBefore(pageParam ?? new Date().toISOString(), interval);

      return read(period.from, period.to);
    },
    initialPageParam: null as string | null,
    getNextPageParam: (page) => (page.candles.length === 0 ? undefined : page.from),
    staleTime: Number.POSITIVE_INFINITY,
    refetchOnWindowFocus: false,
  });
}

const minute = 60_000;

/** How long after a close the server has stored the candle: the backend's `CloseMargin`. */
const closeMargin = 5000;

/**
 * The wait before the next tail read: a minute, or less when a candle closes sooner and the server will have stored it
 * by `closeMargin` after the close, so a closed candle shows within a minute. Weeks and months are not aligned to the
 * epoch and keep the minute.
 */
function untilNextRead(interval: string, now: number): number {
  const seconds = interval === 'W' || interval === 'M' ? undefined : intervalSeconds[interval];

  if (seconds === undefined) {
    return minute;
  }

  const length = seconds * 1000;
  const storedAt = Math.floor((now - closeMargin) / length) * length + length + closeMargin;

  return Math.min(minute, storedAt - now);
}

/** The candles closed since `from`, read again every minute; the first minute needs no request, there is nothing yet. */
function tailQuery(scope: readonly unknown[], interval: string, from: string, read: ReadCandles) {
  return queryOptions({
    queryKey: [...candlesKey(scope, interval), 'tail', from],
    queryFn: () => read(from, new Date().toISOString()),
    initialData: { from, to: from, candles: [], unavailable: false } satisfies CandlesPage,
    initialDataUpdatedAt: () => Date.now(),
    staleTime: minute,
    refetchInterval: () => untilNextRead(interval, Date.now()),
  });
}

/**
 * Where the tail read starts: the end of the newest candle the first page has, so a candle that was not stored yet
 * when the page was read comes with a later read; without candles, the end of the period that was asked for.
 */
function tailStart(first: CandlesPage | undefined, interval: string): string | undefined {
  if (first === undefined) {
    return undefined;
  }

  const newest = first.candles.reduce(
    (latest, item) => Math.max(latest, Date.parse(item.openTime)),
    Number.NEGATIVE_INFINITY,
  );

  return Number.isFinite(newest)
    ? new Date(candleEnd(newest / 1000, interval) * 1000).toISOString()
    : first.to;
}

/**
 * The history of a chart: the pages going back, joined with the candles closed since the first page, which a
 * minute-by-minute read brings. Pages that Bybit could not give are read again every minute. When more than 999 candles
 * closed since then, the history starts over from now. `scope` names the instrument in the query keys; `read` reads a page
 * from the endpoint of the bot or of the pair, and the latest one given is the one used.
 */
export function useCandlePages(scope: readonly unknown[], interval: string, read: ReadCandles) {
  const queryClient = useQueryClient();
  const reading = useRef(read);
  const key = JSON.stringify(scope);

  useEffect(() => {
    reading.current = read;
  }, [read]);

  const readPage = useCallback<ReadCandles>((from, to) => reading.current(from, to), []);
  // The caller builds a new array on every render; the scope is compared by its text.
  const stableScope = useMemo(() => scope, [key]);
  const history = useInfiniteQuery(candlesQuery(stableScope, interval, readPage));
  const newestTo = history.data?.pages[0]?.to;
  const tailFrom = tailStart(history.data?.pages[0], interval);
  const tail = useQuery({
    ...tailQuery(stableScope, interval, tailFrom ?? '', readPage),
    enabled: tailFrom !== undefined,
  });

  const historyUnavailable = history.data?.pages.some((page) => page.unavailable) ?? false;
  const { refetch } = history;

  useEffect(() => {
    if (tailFrom === undefined) {
      return undefined;
    }

    const timer = setInterval(() => {
      // A page that could not be read is empty and stops the paging; reading the pages again once Bybit is back
      // brings its candles and lets the paging go on.
      if (historyUnavailable) {
        void refetch({ cancelRefetch: false });
      }

      if (tooFarBehind(tailFrom, new Date(), interval)) {
        void queryClient.resetQueries({ queryKey: candlesKey(stableScope, interval), exact: true });
      }
    }, minute);

    return () => clearInterval(timer);
  }, [stableScope, interval, tailFrom, queryClient, historyUnavailable, refetch]);

  const pages = useMemo(
    () => [
      ...(history.data?.pages ?? []),
      ...(newestTo !== undefined && tail.data ? [tail.data] : []),
    ],
    [history.data, newestTo, tail.data],
  );
  const candles = useMemo(() => mergeCandles(pages), [pages]);

  return {
    candles,
    unavailable: pages.some((page) => page.unavailable),
    isPending: history.isPending,
    isError: history.isError,
    hasEarlier: history.hasNextPage,
    loadEarlier: () => {
      if (history.hasNextPage && !history.isFetchingNextPage) {
        void history.fetchNextPage();
      }
    },
  };
}
