import type { Graph, GraphNode, GraphWire } from './types';

/** The node of a pin reference `node.pin`. */
export function nodeOf(reference: string): string {
  const dot = reference.indexOf('.');
  return dot === -1 ? reference : reference.slice(0, dot);
}

/** The pin of a pin reference `node.pin`. */
export function pinOf(reference: string): string {
  const dot = reference.indexOf('.');
  return dot === -1 ? '' : reference.slice(dot + 1);
}

export function findNode(graph: Graph, id: string): GraphNode | undefined {
  return graph.nodes.find((node) => node.id === id);
}

/** The wire that comes into an input, if any: an input takes at most one. */
export function wireInto(graph: Graph, node: string, pin: string): GraphWire | undefined {
  return graph.wires.find((wire) => wire.to === `${node}.${pin}`);
}

/**
 * A graph of the right outer form read from unknown JSON, or null: an object with `formatVersion` 1 and arrays of
 * parameters, outputs, nodes and wires. Everything else is the server's check.
 */
export function asGraph(value: unknown): Graph | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return null;
  }

  const candidate = value as Record<string, unknown>;
  if (candidate.formatVersion !== 1 || !Array.isArray(candidate.nodes)) {
    return null;
  }

  for (const field of ['parameters', 'outputs', 'wires']) {
    if (candidate[field] !== undefined && !Array.isArray(candidate[field])) {
      return null;
    }
  }

  const nodes = candidate.nodes as unknown[];
  if (
    !nodes.every(
      (node) => typeof node === 'object' && node !== null && 'id' in node && 'kind' in node,
    )
  ) {
    return null;
  }

  return {
    formatVersion: 1,
    parameters: (candidate.parameters as Graph['parameters'] | undefined) ?? [],
    outputs: (candidate.outputs as Graph['outputs'] | undefined) ?? [],
    nodes: nodes as Graph['nodes'],
    wires: (candidate.wires as Graph['wires'] | undefined) ?? [],
  };
}

/** The graph as JSON text, indented, as the card «JSON» shows it. */
export function graphText(graph: Graph): string {
  return JSON.stringify(graph, null, 2);
}
