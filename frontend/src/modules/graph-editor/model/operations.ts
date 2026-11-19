import { nodeOf, pinOf } from './graph';
import { placesOf } from './layout';
import type {
  GraphDocument,
  GraphNode,
  GraphParameter,
  GraphValue,
  Layout,
  NodeKind,
  Position,
} from './types';

/**
 * The edits of the canvas. Each takes the document and gives a new one; none changes its argument. The first edit
 * pins the places of all nodes, so a node never jumps when another one is added.
 */

/** A free id for a new node of a kind: `compare-1`, `compare-2`, … */
export function newId(document: GraphDocument, kind: string): string {
  const taken = new Set(document.graph.nodes.map((node) => node.id));
  for (let number = 1; ; number += 1) {
    const id = `${kind}-${number}`;
    if (!taken.has(id)) {
      return id;
    }
  }
}

/** Every node pinned where it stands on the canvas now. */
export function pinPlaces(document: GraphDocument, catalog: readonly NodeKind[]): GraphDocument {
  return { ...document, layout: placesOf(document.graph, document.layout, catalog) };
}

export function addNode(
  document: GraphDocument,
  catalog: readonly NodeKind[],
  kind: NodeKind,
  position: Position,
): { document: GraphDocument; id: string } {
  const pinned = pinPlaces(document, catalog);
  const id = newId(pinned, kind.kind);
  const settings = Object.fromEntries(
    kind.settings
      .map((setting) => [setting.name, setting.default ?? setting.values[0]] as const)
      .filter((entry): entry is readonly [string, string] => entry[1] !== undefined),
  );
  const node: GraphNode =
    Object.keys(settings).length > 0 ? { id, kind: kind.kind, settings } : { id, kind: kind.kind };
  // A new node goes before the outputs node, so a graph built on the canvas keeps the order the files write: the
  // outputs node last.
  const outputs = pinned.graph.nodes.findIndex((candidate) => candidate.kind === 'outputs');
  const at = outputs === -1 ? pinned.graph.nodes.length : outputs;
  const nodes = [...pinned.graph.nodes.slice(0, at), node, ...pinned.graph.nodes.slice(at)];
  return {
    id,
    document: {
      graph: { ...pinned.graph, nodes },
      layout: { ...pinned.layout, [id]: freePlace(pinned.layout, position) },
    },
  };
}

/** The place itself, or, when a node already stands there, the first one down and right that no node takes. */
export function freePlace(layout: Layout, position: Position): Position {
  const taken = Object.values(layout);
  let place = position;
  while (
    taken.some((other) => Math.abs(other.x - place.x) < 20 && Math.abs(other.y - place.y) < 20)
  ) {
    place = { x: place.x + 40, y: place.y + 40 };
  }

  return place;
}

/** Removes a node with every wire to and from it. */
export function removeNode(
  document: GraphDocument,
  catalog: readonly NodeKind[],
  id: string,
): GraphDocument {
  const pinned = pinPlaces(document, catalog);
  const layout: Record<string, Position> = { ...pinned.layout };
  delete layout[id];
  return {
    graph: {
      ...pinned.graph,
      nodes: pinned.graph.nodes.filter((node) => node.id !== id),
      wires: pinned.graph.wires.filter(
        (wire) => nodeOf(wire.from) !== id && nodeOf(wire.to) !== id,
      ),
    },
    layout,
  };
}

/**
 * Wires an output to an input. A wire already on the input is replaced, and the value written on the input goes:
 * an input takes one wire, and a wire and a value at once is an error of the format.
 */
export function connect(document: GraphDocument, from: string, to: string): GraphDocument {
  const target = nodeOf(to);
  const pin = pinOf(to);
  return {
    ...document,
    graph: {
      ...document.graph,
      nodes: document.graph.nodes.map((node) =>
        node.id === target ? withoutInput(node, pin) : node,
      ),
      wires: [...document.graph.wires.filter((wire) => wire.to !== to), { from, to }],
    },
  };
}

export function disconnect(document: GraphDocument, from: string, to: string): GraphDocument {
  return {
    ...document,
    graph: {
      ...document.graph,
      wires: document.graph.wires.filter((wire) => !(wire.from === from && wire.to === to)),
    },
  };
}

/** Writes the value of an input without a wire; undefined takes the value away. */
export function setInputValue(
  document: GraphDocument,
  id: string,
  pin: string,
  value: GraphValue | undefined,
): GraphDocument {
  return {
    ...document,
    graph: {
      ...document.graph,
      nodes: document.graph.nodes.map((node) => {
        if (node.id !== id) {
          return node;
        }

        if (value === undefined) {
          return withoutInput(node, pin);
        }

        return { ...node, inputs: { ...node.inputs, [pin]: value } };
      }),
    },
  };
}

export function setSetting(
  document: GraphDocument,
  id: string,
  setting: string,
  value: string,
): GraphDocument {
  return {
    ...document,
    graph: {
      ...document.graph,
      nodes: document.graph.nodes.map((node) =>
        node.id === id ? { ...node, settings: { ...node.settings, [setting]: value } } : node,
      ),
    },
  };
}

/** Moves nodes to new places, rounded to whole pixels so the file keeps short numbers. */
export function moveNodes(
  document: GraphDocument,
  catalog: readonly NodeKind[],
  places: Readonly<Record<string, Position>>,
): GraphDocument {
  const pinned = pinPlaces(document, catalog);
  const rounded = Object.fromEntries(
    Object.entries(places).map(([id, place]) => [
      id,
      { x: Math.round(place.x), y: Math.round(place.y) },
    ]),
  );
  return { ...pinned, layout: { ...pinned.layout, ...rounded } };
}

/** A new id for a node: its wires and its place follow it. An id that is taken or empty changes nothing. */
export function renameNode(
  document: GraphDocument,
  catalog: readonly NodeKind[],
  from: string,
  to: string,
): GraphDocument {
  if (to === '' || to === from || document.graph.nodes.some((node) => node.id === to)) {
    return document;
  }

  const pinned = pinPlaces(document, catalog);
  const moved = (reference: string) =>
    nodeOf(reference) === from ? `${to}.${pinOf(reference)}` : reference;
  const layout: Record<string, Position> = {};
  for (const [id, place] of Object.entries(pinned.layout)) {
    layout[id === from ? to : id] = place;
  }

  return {
    graph: {
      ...pinned.graph,
      nodes: pinned.graph.nodes.map((node) => (node.id === from ? { ...node, id: to } : node)),
      wires: pinned.graph.wires.map((wire) => ({ from: moved(wire.from), to: moved(wire.to) })),
    },
    layout,
  };
}

/**
 * New parameters of the graph. A parameter renamed in place keeps its wires: every wire from `parameters` nodes moves
 * to the new name; a parameter taken away takes its wires.
 */
export function setParameters(
  document: GraphDocument,
  parameters: readonly GraphParameter[],
  renamed: Readonly<Record<string, string>> = {},
): GraphDocument {
  const kept = new Set(parameters.map((parameter) => parameter.name));
  const fromParameters = new Set(
    document.graph.nodes.filter((node) => node.kind === 'parameters').map((node) => node.id),
  );
  const wires = document.graph.wires.flatMap((wire) => {
    const node = nodeOf(wire.from);
    if (!fromParameters.has(node)) {
      return [wire];
    }

    const name = renamed[pinOf(wire.from)] ?? pinOf(wire.from);
    return kept.has(name) ? [{ from: `${node}.${name}`, to: wire.to }] : [];
  });
  return { ...document, graph: { ...document.graph, parameters, wires } };
}

/** The layout dropped, so every node gets the place of the layout by depth: «Разложить». */
export function relayout(document: GraphDocument): GraphDocument {
  return { ...document, layout: {} };
}

/** The positions of the layout that belong to the nodes of the graph. */
export function layoutOfNodes(document: GraphDocument): Layout {
  const ids = new Set(document.graph.nodes.map((node) => node.id));
  return Object.fromEntries(Object.entries(document.layout).filter(([id]) => ids.has(id)));
}

function withoutInput(node: GraphNode, pin: string): GraphNode {
  if (node.inputs?.[pin] === undefined) {
    return node;
  }

  const inputs = { ...node.inputs };
  delete inputs[pin];
  if (Object.keys(inputs).length === 0) {
    const { inputs: _dropped, ...rest } = node;
    void _dropped;
    return rest;
  }

  return { ...node, inputs };
}
