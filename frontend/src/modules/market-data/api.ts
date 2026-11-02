import { queryOptions } from '@tanstack/react-query';

import { api, unwrap } from '@/shared/api/client';

/** Instruments of a category whose symbol or coin contains the text; the server caches the list for 5 minutes. */
export function instrumentsQuery(category: string, query: string) {
  return queryOptions({
    queryKey: ['instruments', category, query],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/market-data/instruments', {
          params: { query: { Category: category, query } },
        }),
      ),
    staleTime: 5 * 60_000,
  });
}
