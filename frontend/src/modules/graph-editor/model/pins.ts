import { signalInfo } from './signals';
import type { Catalog, Graph, GraphNode, NodeKind, Pin, PinType } from './types';

/** The kind of a node in the catalog. */
export function kindOf(node: GraphNode, catalog: readonly NodeKind[]): NodeKind | undefined {
  return catalog.find((kind) => kind.kind === node.kind);
}

/**
 * The pins of a node: from the catalog, or, for `parameters` and `outputs`, from the graph — a parameter is an output
 * of `parameters`, an output of the graph is an input of `outputs` — or, for `signal`, from the version of the signal
 * the node points at: an input per parameter, which takes the value of the parameter when nothing is given to it.
 */
export function pinsOf(
  node: GraphNode,
  catalog: Catalog,
  graph: Graph,
): { inputs: Pin[]; outputs: Pin[] } {
  const kind = kindOf(node, catalog);
  if (kind?.pinsFromGraph === 'signal') {
    const parameters = signalInfo(node, catalog)?.parameters ?? [];
    return {
      inputs: parameters.map(({ name, type, value }) => ({ name, type, fallback: value })),
      outputs: (kind.outputs ?? []).map((pin) => ({ name: pin.name, type: pin.type as PinType })),
    };
  }

  if (kind?.pinsFromGraph === 'parameters') {
    return { inputs: [], outputs: graph.parameters.map(({ name, type }) => ({ name, type })) };
  }

  if (kind?.pinsFromGraph === 'outputs') {
    return { inputs: graph.outputs.map(({ name, type }) => ({ name, type })), outputs: [] };
  }

  return {
    inputs: (kind?.inputs ?? []).map((pin) =>
      pin.many
        ? { name: pin.name, type: pin.type as PinType, many: true }
        : { name: pin.name, type: pin.type as PinType },
    ),
    outputs: (kind?.outputs ?? []).map((pin) => ({ name: pin.name, type: pin.type as PinType })),
  };
}

/** Whether the input `node.pin` takes several wires. */
export function takesMany(graph: Graph, catalog: Catalog, reference: string): boolean {
  const dot = reference.indexOf('.');
  const node = graph.nodes.find((candidate) => candidate.id === reference.slice(0, dot));
  return (
    node !== undefined &&
    pinsOf(node, catalog, graph).inputs.some(
      (pin) => pin.name === reference.slice(dot + 1) && pin.many === true,
    )
  );
}

/** The type of a pin reference `node.pin` on the given side, if the node and the pin exist. */
export function typeOf(
  graph: Graph,
  catalog: Catalog,
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
