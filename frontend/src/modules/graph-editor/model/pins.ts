import type { Graph, GraphNode, NodeKind, Pin, PinType } from './types';

/** The kind of a node in the catalog. */
export function kindOf(node: GraphNode, catalog: readonly NodeKind[]): NodeKind | undefined {
  return catalog.find((kind) => kind.kind === node.kind);
}

/**
 * The pins of a node: from the catalog, or, for `parameters` and `outputs`, from the graph — a parameter is an output
 * of `parameters`, an output of the graph is an input of `outputs`.
 */
export function pinsOf(
  node: GraphNode,
  catalog: readonly NodeKind[],
  graph: Graph,
): { inputs: Pin[]; outputs: Pin[] } {
  const kind = kindOf(node, catalog);
  if (kind?.pinsFromGraph === 'parameters') {
    return { inputs: [], outputs: graph.parameters.map(({ name, type }) => ({ name, type })) };
  }

  if (kind?.pinsFromGraph === 'outputs') {
    return { inputs: graph.outputs.map(({ name, type }) => ({ name, type })), outputs: [] };
  }

  return {
    inputs: (kind?.inputs ?? []).map((pin) => ({ name: pin.name, type: pin.type as PinType })),
    outputs: (kind?.outputs ?? []).map((pin) => ({ name: pin.name, type: pin.type as PinType })),
  };
}

/** The type of a pin reference `node.pin` on the given side, if the node and the pin exist. */
export function typeOf(
  graph: Graph,
  catalog: readonly NodeKind[],
  reference: string,
  side: 'inputs' | 'outputs',
): PinType | undefined {
  const dot = reference.indexOf('.');
  const node = graph.nodes.find((candidate) => candidate.id === reference.slice(0, dot));
  return (
    node &&
    pinsOf(node, catalog, graph)[side].find((pin) => pin.name === reference.slice(dot + 1))?.type
  );
}
