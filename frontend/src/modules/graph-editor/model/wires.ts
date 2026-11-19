import { nodeOf } from './graph';
import { typeOf } from './pins';
import type { Graph, NodeKind } from './types';

/**
 * Whether a wire may go from an output to an input (spec signal-constructor «Правка графа на холсте»): both pins
 * exist, they are on different nodes and of one type. A wire to an input that has one replaces it, so that is allowed.
 */
export function canConnect(
  graph: Graph,
  catalog: readonly NodeKind[],
  from: string,
  to: string,
): boolean {
  if (nodeOf(from) === nodeOf(to)) {
    return false;
  }

  const source = typeOf(graph, catalog, from, 'outputs');
  const target = typeOf(graph, catalog, to, 'inputs');
  return source !== undefined && source === target;
}
