import { api, type Schemas, unwrap } from './client';
import { pageOf, pageQuery, type PageRequest } from './lists';

export type MarketPair = Schemas['MarketPairResponse'];
export type MarketPage = Schemas['MarketPageResponse'];
export type IdeaBadge = Schemas['IdeaBadgeResponse'];
export type PairChart = Schemas['PairChartResponse'];
export type PairOpenInterest = Schemas['PairOpenInterestResponse'];
export type ChartCandle = Schemas['ChartCandle'];
export type ChartPoint = Schemas['ChartPoint'];
export type LiveCandle = Schemas['LiveCandleResponse'];

/** The kind of a pair as the API names it (change add-instrument-kind-filter): crypto, or a contract on a share. */
export type InstrumentKind = 'crypto' | 'stock';

/** The choices above the table of «Рынок». */
export interface MarketChoices {
  readonly search?: string;
  readonly ideasOnly?: boolean;
  readonly strategy?: string;
  readonly timeframe?: string;
  /** The kind of the pair; every kind when not given. */
  readonly kind?: InstrumentKind;
}

export async function listMarketPairs(
  request: PageRequest,
  choices: MarketChoices = {},
): Promise<MarketPage> {
  const query = {
    ...pageQuery(request),
    ...(choices.search ? { search: choices.search } : {}),
    ...(choices.ideasOnly ? { ideasOnly: true } : {}),
    ...(choices.strategy ? { strategy: choices.strategy } : {}),
    ...(choices.timeframe ? { timeframe: choices.timeframe } : {}),
    ...(choices.kind ? { kind: choices.kind } : {}),
  };
  return pageOf(await api.GET('/api/market/pairs', { params: { query } }));
}

/** The pair of the set, or null when the set has no such pair (`404`). */
export async function getMarketPair(symbol: string): Promise<MarketPair | null> {
  const result = await api.GET('/api/market/pairs/{symbol}', { params: { path: { symbol } } });
  return result.response.status === 404 ? null : unwrap(result);
}

/** A page of 500 closed candles before `before`, with the payments of that time. */
export async function getPairChart(
  symbol: string,
  timeframe: string,
  before?: number,
): Promise<PairChart> {
  const query = before === undefined ? { timeframe } : { timeframe, before };
  return pageOf(
    await api.GET('/api/market/pairs/{symbol}/chart', { params: { path: { symbol }, query } }),
  );
}

/**
 * The open interest of the page of the chart with the same `before`, one point a candle (task 19.7): its own request, so
 * the candles and the payments do not wait for it.
 */
export async function getPairOpenInterest(
  symbol: string,
  timeframe: string,
  before?: number,
): Promise<PairOpenInterest> {
  const query = before === undefined ? { timeframe } : { timeframe, before };
  return pageOf(
    await api.GET('/api/market/pairs/{symbol}/chart/open-interest', {
      params: { path: { symbol }, query },
    }),
  );
}

/** The address of the stream of the live candle of a pair on a timeframe. */
export function liveCandleUrl(symbol: string, timeframe: string): string {
  return `/api/market/pairs/${encodeURIComponent(symbol)}/live?timeframe=${encodeURIComponent(timeframe)}`;
}
