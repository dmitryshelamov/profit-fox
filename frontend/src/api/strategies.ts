import { api, type Schemas, unwrap } from './client';

export type StrategyListItem = Schemas['StrategyListItem'];
export type StrategyResponse = Schemas['StrategyResponse'];

export async function listStrategies(): Promise<StrategyListItem[]> {
  return unwrap(await api.GET('/api/strategies'));
}

/** The node catalog of a strategy (spec strategy-constructor «Каталог узлов стратегии»). */
export async function listStrategyNodeKinds() {
  return unwrap(await api.GET('/api/nodes/strategy'));
}
