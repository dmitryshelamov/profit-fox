import { useQueries } from '@tanstack/react-query';

import { backtestQuery, backtestResultQuery, resultDetailsQuery } from '@/modules/backtests';
import { botQuery } from '@/modules/bots';
import type { ComparisonItem } from '@/modules/comparison';
import { ApiError } from '@/shared/api/client';

const notFound = (error: unknown) => error instanceof ApiError && error.status === 404;

/** Everything the comparison shows for each item of the set, loaded in parallel; each part arrives when it is ready. */
export function useCompared(items: readonly ComparisonItem[]) {
  const backtests = useQueries({ queries: items.map((item) => backtestQuery(item.backtestId)) });
  const results = useQueries({
    queries: items.map((item) => backtestResultQuery(item.backtestId, item.combination)),
  });
  const details = useQueries({
    queries: items.map((item) => resultDetailsQuery(item.backtestId, item.combination)),
  });
  const bots = useQueries({
    queries: items.map((_, index) => ({
      ...botQuery(backtests[index]?.data?.botId ?? ''),
      enabled: backtests[index]?.data !== undefined,
    })),
  });

  return items.map((item, index) => ({
    item,
    backtest: backtests[index]?.data,
    result: results[index]?.data,
    bot: bots[index]?.data,
    details: details[index]?.data,
    detailsLoading: details[index]?.isPending ?? false,
    detailsError: details[index]?.error ?? undefined,
    missing: notFound(backtests[index]?.error) || notFound(results[index]?.error),
  }));
}
