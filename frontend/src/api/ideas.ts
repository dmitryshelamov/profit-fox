import { api, type Schemas, unwrap } from './client';
import { pageOf, pageQuery, type PageRequest } from './lists';
import { type Written, written } from './signals';

export type Idea = Schemas['IdeaResponse'];
export type IdeaPage = Schemas['IdeaPageResponse'];
export type IdeaSummary = Schemas['IdeaSummaryResponse'];
export type IdeaSummaryPage = Schemas['IdeaSummaryPageResponse'];
export type ScannerState = Schemas['ScannerStateResponse'];
export type StrategyIdeas = Schemas['StrategyIdeasResponse'];
export type SignalLabel = Schemas['SignalLabelResponse'];

/** The choices above a table of ideas. */
export interface IdeaChoices {
  readonly search?: string;
  readonly strategy?: string;
  readonly timeframe?: string;
  readonly side?: string;
  readonly label?: string;
  readonly positive?: boolean;
  readonly withStop?: boolean;
  readonly exitReason?: string;
  readonly period?: number;
  readonly state?: string;
}

function choicesQuery(choices: IdeaChoices) {
  return Object.fromEntries(
    Object.entries(choices).filter(
      ([, value]) => value !== undefined && value !== '' && value !== false,
    ),
  );
}

export async function listIdeas(
  state: 'open' | 'closed',
  request: PageRequest,
  choices: IdeaChoices = {},
): Promise<IdeaPage> {
  const query = { ...pageQuery(request), ...choicesQuery(choices), state };
  return pageOf(await api.GET('/api/ideas', { params: { query } }));
}

export async function summarizeIdeas(
  request: PageRequest,
  choices: IdeaChoices = {},
): Promise<IdeaSummaryPage> {
  const query = { ...pageQuery(request), ...choicesQuery(choices) };
  return pageOf(await api.GET('/api/ideas/summary', { params: { query } }));
}

export async function getScannerState(): Promise<ScannerState> {
  return unwrap(await api.GET('/api/ideas/state'));
}

export async function listPairIdeas(
  symbol: string,
  request: PageRequest,
  choices: IdeaChoices = {},
): Promise<IdeaPage> {
  const query = { ...pageQuery(request), ...choicesQuery(choices) };
  return pageOf(
    await api.GET('/api/market/pairs/{symbol}/ideas', { params: { path: { symbol }, query } }),
  );
}

/** Turns the ideas of a strategy on or off on the timeframes (spec strategy-ideas «Включение идей у стратегии»). */
export async function setStrategyIdeas(
  name: string,
  enabled: boolean,
  timeframes: readonly string[],
): Promise<Written<StrategyIdeas>> {
  return written(
    await api.PUT('/api/strategies/{name}/ideas', {
      params: { path: { name } },
      body: { enabled, timeframes: [...timeframes] },
    }),
  );
}

/** Turns the label of a signal on or off (spec idea-labels «Метка сигнала»). */
export async function setSignalLabel(
  name: string,
  enabled: boolean,
  text: string,
  timeframe: string,
): Promise<Written<SignalLabel>> {
  return written(
    await api.PUT('/api/signals/{name}/label', {
      params: { path: { name } },
      body: { enabled, text, timeframe },
    }),
  );
}
