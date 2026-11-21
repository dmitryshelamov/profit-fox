import type { GraphDocument } from './types';

/** Whether two documents are the same: the graph and the positions, whatever the order of keys. */
export function sameDocument(a: GraphDocument, b: GraphDocument): boolean {
  return canonical(a.graph) === canonical(b.graph) && canonical(a.layout) === canonical(b.layout);
}

/** How many nodes and wires differ between two documents, as the question about a draft tells it. */
export function differences(a: GraphDocument, b: GraphDocument): { nodes: number; wires: number } {
  const nodes = new Map(a.graph.nodes.map((node) => [node.id, canonical(node)]));
  const otherNodes = new Map(b.graph.nodes.map((node) => [node.id, canonical(node)]));
  const changedNodes = new Set<string>();
  for (const [id, text] of nodes) {
    if (otherNodes.get(id) !== text) {
      changedNodes.add(id);
    }
  }

  for (const id of otherNodes.keys()) {
    if (!nodes.has(id)) {
      changedNodes.add(id);
    }
  }

  const wires = new Set(a.graph.wires.map((wire) => `${wire.from}>${wire.to}`));
  const otherWires = new Set(b.graph.wires.map((wire) => `${wire.from}>${wire.to}`));
  const changedWires =
    [...wires].filter((wire) => !otherWires.has(wire)).length +
    [...otherWires].filter((wire) => !wires.has(wire)).length;
  return { nodes: changedNodes.size, wires: changedWires };
}

/** JSON with the keys of every object sorted, so equal values give equal text. */
export function canonical(value: unknown): string {
  return JSON.stringify(sorted(value));
}

function sorted(value: unknown): unknown {
  if (Array.isArray(value)) {
    return value.map(sorted);
  }

  if (typeof value === 'object' && value !== null) {
    return Object.fromEntries(
      Object.keys(value)
        .sort()
        .map((key) => [key, sorted((value as Record<string, unknown>)[key])]),
    );
  }

  return value;
}
