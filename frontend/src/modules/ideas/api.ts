import { keepPreviousData, queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';

import { api, unwrap } from '@/shared/api/client';
import { jsonNumberOf } from '@/shared/api/jsonNumber';

import type { IdeaSort } from './model/sort';

/** What the list of ideas is asked for; an empty text means no limit. */
export interface IdeaFilters {
  readonly setup: string;
  readonly interval: string;
  readonly type: string;
  readonly side: string;
  readonly category: string;
  readonly nonNegativeExpectancy: boolean;
}

export const noIdeaFilters: IdeaFilters = {
  setup: '',
  interval: '',
  type: '',
  side: '',
  category: '',
  nonNegativeExpectancy: false,
};

const given = (text: string) => (text === '' ? undefined : text);

/** What a page of the list of ideas is asked for: the filters, the saved filter, the order, and the number and the size of the page. */
export interface IdeasRequest {
  readonly filters: IdeaFilters;
  readonly savedFilterId: string | null;
  readonly sort: IdeaSort;
  readonly page: number;
  readonly pageSize: number;
}

/**
 * One page of the open ideas and the ones closed in the last 24 hours, read again every 15 seconds; the API filters, sorts and
 * cuts it. The old page stays on the screen while a new one loads.
 */
export function ideasQuery(request: IdeasRequest, enabled: boolean) {
  const { filters, savedFilterId, sort, page, pageSize } = request;

  return queryOptions({
    queryKey: ['ideas', 'page', request],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/ideas', {
          params: {
            query: {
              Setup: given(filters.setup),
              Interval: given(filters.interval),
              Type: given(filters.type),
              Side: given(filters.side),
              Category: given(filters.category),
              NonNegativeExpectancy: filters.nonNegativeExpectancy ? true : undefined,
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

/** One idea by its id, such as one of the address that left the list of its pair. */
export function ideaQuery(id: string) {
  return queryOptions({
    queryKey: ['ideas', 'one', id],
    queryFn: async () => unwrap(await api.GET('/api/ideas/{id}', { params: { path: { id } } })),
    retry: false,
  });
}

/** The ideas of one pair, in the order of the list. */
export function pairIdeasQuery(category: string, symbol: string) {
  return queryOptions({
    queryKey: ['ideas', 'pair', category, symbol],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/market/{category}/{symbol}/ideas', {
          params: { path: { category, symbol } },
        }),
      ),
    refetchInterval: 15_000,
  });
}

/** The support and resistance zones of a pair on an interval of the setups. */
export function zonesQuery(category: string, symbol: string, interval: string) {
  return queryOptions({
    queryKey: ['zones', category, symbol, interval],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/market/{category}/{symbol}/zones', {
          params: { path: { category, symbol }, query: { interval } },
        }),
      ),
  });
}

export interface OwnIdeaInput {
  readonly type: string;
  readonly interval: string;
  readonly side: string;
}

/** An own idea from the price now; asking is a calculation and changes nothing, so it is a query. */
export function ownIdeaQuery(category: string, symbol: string, input: OwnIdeaInput) {
  return queryOptions({
    queryKey: ['own-idea', category, symbol, input],
    queryFn: async () =>
      unwrap(
        await api.POST('/api/market/{category}/{symbol}/own-idea', {
          params: { path: { category, symbol } },
          body: input,
        }),
      ),
    staleTime: 15_000,
  });
}

/** The report of the calibration of a window of days, 90 or 30, and of a category, all of them without one. */
export function calibrationQuery(
  days: 90 | 30,
  category: string | null,
  scope: 'all' | 'fresh' = 'all',
) {
  return queryOptions({
    queryKey: ['ideas', 'calibration', days, category, scope],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/ideas/calibration', {
          params: {
            query: {
              Days: String(days),
              Category: category ?? undefined,
              Scope: scope === 'fresh' ? scope : undefined,
            },
          },
        }),
      ),
    placeholderData: keepPreviousData,
  });
}

/** The exit rules of every type × interval, with the defaults among them, and the grid of lifetimes of each interval. */
export function exitRulesQuery() {
  return queryOptions({
    queryKey: ['ideas', 'exit-rules'],
    queryFn: async () => unwrap(await api.GET('/api/ideas/exit-rules')),
  });
}

/** A candidate exit rule of a type × interval: the lifetime in candles, the target in R as a decimal text and the entry window, `null` where the type has none. */
export interface ExitRuleCandidate {
  readonly type: string;
  readonly interval: string;
  readonly life: number;
  readonly target: string | null;
  readonly window: number | null;
}

/** What the candidate would have done against the rule now; a calculation that changes nothing. The old answer stays while a new one loads. */
export function exitRulePreviewQuery(candidate: ExitRuleCandidate) {
  return queryOptions({
    queryKey: ['ideas', 'exit-rules', 'preview', candidate],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/ideas/exit-rules/preview', {
          params: {
            query: {
              type: candidate.type,
              interval: candidate.interval,
              life: candidate.life,
              target: candidate.target ?? undefined,
              window: candidate.window ?? undefined,
            },
          },
        }),
      ),
    placeholderData: keepPreviousData,
  });
}

async function applyExitRule(candidate: ExitRuleCandidate) {
  return unwrap(
    await api.PUT('/api/ideas/exit-rules/{type}/{interval}', {
      params: { path: { type: candidate.type, interval: candidate.interval } },
      body: {
        life: candidate.life,
        target:
          candidate.target === null ? null : (jsonNumberOf(candidate.target) as unknown as number),
        window: candidate.window,
      },
    }),
  );
}

/** Applies the candidate to its type × interval; the rules and every preview are read again. */
export function useApplyExitRule() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: applyExitRule,
    onSuccess: () => client.invalidateQueries({ queryKey: ['ideas', 'exit-rules'] }),
  });
}
