import { nodeOf } from './graph';
import { takesMany, typeOf } from './pins';
import type { Catalog, Graph, NodeKind } from './types';

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

/**
 * The wires back into the position (spec strategy-constructor «Провод-возврат и расстановка»): a wire into an input that
 * takes several wires from a node that gets something from the node of that input, directly or through other nodes. The
 * layout leaves them out, so a node that reads the position stands right of it; the canvas draws them as an arc. The
 * ids are those of the edges, `from->to`.
 */
export function backWires(graph: Graph, catalog: Catalog): Set<string> {
  const many = graph.wires.filter((wire) => takesMany(graph, catalog, wire.to));
  const forward = graph.wires.filter((wire) => !many.includes(wire));
  const next = new Map<string, string[]>();
  for (const wire of forward) {
    next.set(nodeOf(wire.from), [...(next.get(nodeOf(wire.from)) ?? []), nodeOf(wire.to)]);
  }

  const reached = (start: string): Set<string> => {
    const seen = new Set<string>();
    const queue = [...(next.get(start) ?? [])];
    while (queue.length > 0) {
      const id = queue.shift()!;
      if (!seen.has(id)) {
        seen.add(id);
        queue.push(...(next.get(id) ?? []));
      }
    }

    return seen;
  };

  return new Set(
    many
      .filter((wire) => reached(nodeOf(wire.to)).has(nodeOf(wire.from)))
      .map((wire) => `${wire.from}->${wire.to}`),
  );
}
