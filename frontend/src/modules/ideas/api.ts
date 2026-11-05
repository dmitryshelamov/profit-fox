import { keepPreviousData, queryOptions } from '@tanstack/react-query';

import { api, unwrap } from '@/shared/api/client';

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

/** The open ideas and the ones closed in the last 24 hours, by expectancy, read again every 15 seconds. */
export function ideasQuery(filters: IdeaFilters = noIdeaFilters) {
  return queryOptions({
    queryKey: ['ideas', filters],
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
            },
          },
        }),
      ),
    placeholderData: keepPreviousData,
    refetchInterval: 15_000,
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
