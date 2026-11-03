import {
  infiniteQueryOptions,
  queryOptions,
  useInfiniteQuery,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { useEffect, useMemo } from 'react';

import {
  candleEnd,
  intervalSeconds,
  mergeCandles,
  pageBefore,
  tooFarBehind,
} from '@/modules/market-chart';
import { api, type Schemas, unwrap } from '@/shared/api/client';
import type { JsonNumber } from '@/shared/api/jsonNumber';

export const botsQuery = queryOptions({
  queryKey: ['bots'],
  queryFn: async () => unwrap(await api.GET('/api/bots')),
});

/** Whether a bot trades or waits for its cancels: it cannot be changed then. */
export function isWorking(status: string | undefined): boolean {
  return status === 'running' || status === 'paused' || status === 'stopping';
}

/** Whether a Worker keeps the bot, also to close its position: its page and the list then follow it every 5 seconds. */
export function isFollowed(bot: { status: string; closeRequested: boolean } | undefined): boolean {
  return bot !== undefined && (isWorking(bot.status) || bot.closeRequested);
}

export function botQuery(id: string) {
  return queryOptions({
    queryKey: ['bots', id],
    queryFn: async () => unwrap(await api.GET('/api/bots/{id}', { params: { path: { id } } })),
  });
}

/** What the form sends: parameter values by name, the balance as the text typed, which the API reads as a decimal. */
export interface BotInput {
  readonly name: string;
  readonly strategyId?: string;
  readonly parameterValues: Readonly<Record<string, JsonNumber | string>>;
  readonly category: string;
  readonly symbol: string;
  readonly interval: string;
  readonly balance: string;
  /** The exchange account the bot will trade with; `null` for none. */
  readonly exchangeAccountId: string | null;
}

function bodyOf(input: BotInput): Schemas['BotBody'] {
  return {
    ...input,
    parameterValues: input.parameterValues as unknown as Schemas['JsonElement'],
    balance: input.balance.trim(),
  };
}

export async function createBot(input: BotInput): Promise<string> {
  return unwrap(await api.POST('/api/bots', { body: bodyOf(input) })).id;
}

export async function updateBot(id: string, input: BotInput): Promise<void> {
  unwrap(await api.PUT('/api/bots/{id}', { params: { path: { id } }, body: bodyOf(input) }));
}

export async function deleteBot(id: string): Promise<void> {
  unwrap(await api.DELETE('/api/bots/{id}', { params: { path: { id } } }));
}

export function botPositionsQuery(id: string) {
  return queryOptions({
    queryKey: ['bots', id, 'positions'],
    queryFn: async () =>
      unwrap(await api.GET('/api/bots/{id}/positions', { params: { path: { id } } })),
  });
}

export type BotAction = 'start' | 'pause' | 'resume' | 'stop' | 'close-position';

/** An action of live trading; a refusal comes as an `ApiError` with the reason of the server. */
export async function actOnBot(id: string, action: BotAction): Promise<void> {
  const options = { params: { path: { id } } };

  switch (action) {
    case 'start':
      unwrap(await api.POST('/api/bots/{id}/start', options));
      break;
    case 'pause':
      unwrap(await api.POST('/api/bots/{id}/pause', options));
      break;
    case 'resume':
      unwrap(await api.POST('/api/bots/{id}/resume', options));
      break;
    case 'stop':
      unwrap(await api.POST('/api/bots/{id}/stop', options));
      break;
    case 'close-position':
      unwrap(await api.POST('/api/bots/{id}/close-position', options));
      break;
  }
}

/** A page of closed candles with the period that was asked for: the response does not repeat it. */
export interface BotCandlesPage {
  readonly from: string;
  readonly to: string;
  readonly candles: Schemas['CandleResponse'][];
  readonly unavailable: boolean;
}

async function botCandles(
  id: string,
  interval: string,
  from: string,
  to: string,
): Promise<BotCandlesPage> {
  const page = unwrap(
    await api.GET('/api/bots/{id}/candles', {
      params: { path: { id }, query: { From: from, To: to, Interval: interval } },
    }),
  );

  return { from, to, candles: page.candles, unavailable: page.unavailable };
}

const candlesKey = (id: string, interval: string) => ['bots', id, 'candles', interval] as const;

/**
 * The closed candles of the bot's instrument, a page of 500 going back from now at a time. Paging stops at a page without
 * candles: the history starts there or cannot be read.
 */
export function botCandlesQuery(id: string, interval: string) {
  return infiniteQueryOptions({
    queryKey: candlesKey(id, interval),
    queryFn: async ({ pageParam }) => {
      const period = pageBefore(pageParam ?? new Date().toISOString(), interval);

      return botCandles(id, interval, period.from, period.to);
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
function botTailQuery(id: string, interval: string, from: string) {
  return queryOptions({
    queryKey: [...candlesKey(id, interval), 'tail', from],
    queryFn: () => botCandles(id, interval, from, new Date().toISOString()),
    initialData: { from, to: from, candles: [], unavailable: false } satisfies BotCandlesPage,
    initialDataUpdatedAt: () => Date.now(),
    staleTime: minute,
    refetchInterval: () => untilNextRead(interval, Date.now()),
  });
}

/**
 * Where the tail read starts: the end of the newest candle the first page has, so a candle that was not stored yet
 * when the page was read comes with a later read; without candles, the end of the period that was asked for.
 */
function tailStart(first: BotCandlesPage | undefined, interval: string): string | undefined {
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
 * The history of the bot's chart: the pages going back, joined with the candles closed since the first page, which a
 * minute-by-minute read brings. Pages that Bybit could not give are read again every minute. When more than 999 candles closed since then, the history starts over from now.
 */
export function useBotCandles(id: string, interval: string) {
  const queryClient = useQueryClient();
  const history = useInfiniteQuery(botCandlesQuery(id, interval));
  const newestTo = history.data?.pages[0]?.to;
  const tailFrom = tailStart(history.data?.pages[0], interval);
  const tail = useQuery({
    ...botTailQuery(id, interval, tailFrom ?? ''),
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
        void queryClient.resetQueries({ queryKey: candlesKey(id, interval), exact: true });
      }
    }, minute);

    return () => clearInterval(timer);
  }, [id, interval, tailFrom, queryClient, historyUnavailable, refetch]);

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
