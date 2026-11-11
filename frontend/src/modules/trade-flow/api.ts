import { queryOptions } from '@tanstack/react-query';

import { pageBefore } from '@/modules/market-chart';
import { api, unwrap } from '@/shared/api/client';

const minute = 60_000;

/**
 * The bars of buys and sells of a pair on the interval of its chart over the period of the first page of its candles, read again every minute so
 * the closed bars follow. `coveredUntil` is the minute the API cut the period at: the live minutes from it on are not in the bars yet.
 */
export function tradeFlowQuery(category: string, symbol: string, interval: string) {
  return queryOptions({
    queryKey: ['market', category, symbol, 'trade-flow', interval],
    queryFn: async () => {
      const to = new Date().toISOString();
      const { from } = pageBefore(to, interval);
      const answer = unwrap(
        await api.GET('/api/market/{category}/{symbol}/trade-flow', {
          params: { path: { category, symbol }, query: { Interval: interval, From: from, To: to } },
        }),
      );

      return {
        ...answer,
        from,
        coveredUntil: new Date(Math.floor(Date.parse(to) / minute) * minute).toISOString(),
      };
    },
    refetchInterval: minute,
  });
}
