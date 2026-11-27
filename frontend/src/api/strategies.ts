import type { StrategyKind } from '@/modules/graph-editor/document';

import { api, type Schemas, unwrap } from './client';

export type StrategyListItem = Schemas['StrategyListItem'];
export type StrategyResponse = Schemas['StrategyResponse'];

export async function listStrategies(): Promise<StrategyListItem[]> {
  return unwrap(await api.GET('/api/strategies'));
}

/**
 * The node catalog of a strategy of a kind (spec strategy-constructor «Каталог узлов стратегии», «Вид в списке и в
 * редакторе»): a strategy of ideas gets no node its kind cannot have.
 */
export async function listStrategyNodeKinds(kind: StrategyKind) {
  return unwrap(await api.GET('/api/nodes/strategy', { params: { query: { kind } } }));
}
