import { timeframes } from '@/preview/preview';
import { Page, PageTitle } from '@/shared/ui/Page';
import { pageSearch } from '@/shared/ui/useListSettings';

/** The address of the page of a pair: the timeframe of the chart, the chosen idea and the page of its ideas. */
export interface PairSearch {
  readonly timeframe?: string;
  readonly idea?: string;
  readonly page?: number;
}

export function pairSearch(search: Record<string, unknown>): PairSearch {
  return {
    ...(typeof search.timeframe === 'string' &&
    (timeframes as readonly string[]).includes(search.timeframe)
      ? { timeframe: search.timeframe }
      : {}),
    ...(typeof search.idea === 'string' && search.idea !== '' ? { idea: search.idea } : {}),
    ...pageSearch(search),
  };
}

/** The address without the parts that are not set, so the first page and no idea leave nothing behind. */
export function clean(search: PairSearch): PairSearch {
  return Object.fromEntries(
    Object.entries(search).filter(
      ([key, value]) => value !== undefined && !(key === 'page' && value === 1),
    ),
  );
}

/** The page of a pair (spec market-overview «Страница пары»). */
export function PairPage({
  symbol,
}: {
  readonly symbol: string;
  readonly timeframe: string;
  readonly idea?: string;
  readonly page: number;
  readonly onSearch: (change: PairSearch) => void;
}) {
  return (
    <Page>
      <PageTitle title={symbol} />
    </Page>
  );
}
