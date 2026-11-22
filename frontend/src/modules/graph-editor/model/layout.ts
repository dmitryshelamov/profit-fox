import { nodeOf } from './graph';
import { pinsOf } from './pins';
import type { Graph, GraphNode, Layout, NodeKind } from './types';
import { backWires } from './wires';

/** The width of a node on the canvas: fixed, so the layout knows it without measuring. */
export const nodeWidth = 200;

/** A signal node is wider: its header holds the name of the signal, the badge of its version and the id of the node. */
export const signalNodeWidth = 260;

export function widthOf(node: Pick<GraphNode, 'kind'>): number {
  return node.kind === 'signal' ? signalNodeWidth : nodeWidth;
}

const columnGap = 80;
const rowGap = 40;
const headerHeight = 31;
const settingsHeight = 25;
const pinsPadding = 8;
const pinRowHeight = 24;

/**
 * Places every node in columns left to right (spec signal-constructor «Расположение узлов отдельно от графа»): the
 * column of a node is its depth, the longest path of wires to it from the nodes without incoming wires; in a column
 * nodes stand one under another in the order of the graph. A column is as wide as its widest node, so the gap between
 * columns is the same everywhere. The same graph always gets the same places.
 */
export function layoutGraph(graph: Graph, catalog: readonly NodeKind[]): Layout {
  const depths = depthsOf(graph, backWires(graph, catalog));
  const widths: number[] = [];
  for (const node of graph.nodes) {
    const depth = depths.get(node.id) ?? 0;
    widths[depth] = Math.max(widths[depth] ?? 0, widthOf(node));
  }

  const lefts: number[] = [];
  for (let depth = 0, left = 0; depth < widths.length; depth += 1) {
    lefts[depth] = left;
    left += (widths[depth] ?? nodeWidth) + columnGap;
  }

  const filled = new Map<number, number>();
  const places: Record<string, { x: number; y: number }> = {};
  for (const node of graph.nodes) {
    const depth = depths.get(node.id) ?? 0;
    const top = filled.get(depth) ?? 0;
    places[node.id] = { x: lefts[depth] ?? 0, y: top };
    filled.set(depth, top + nodeHeight(node, catalog, graph) + rowGap);
  }

  return places;
}

/** Every node with a place: its own from the layout, or the one of the layout by depth. */
export function placesOf(graph: Graph, layout: Layout, catalog: readonly NodeKind[]): Layout {
  const missing = graph.nodes.some((node) => layout[node.id] === undefined);
  return missing ? { ...layoutGraph(graph, catalog), ...layout } : layout;
}

export function nodeHeight(node: GraphNode, catalog: readonly NodeKind[], graph: Graph): number {
  const pins = pinsOf(node, catalog, graph);
  const rows = Math.max(pins.inputs.length, pins.outputs.length, 1);
  const settings = Object.keys(node.settings ?? {}).length > 0 ? settingsHeight : 0;
  return headerHeight + settings + pinsPadding + rows * pinRowHeight;
}

/**
 * The depth of each node: a wire back into the path being walked (a cycle of an invalid graph) adds none, and the wires
 * back into the position (`back`, by the ids of their edges) are left out.
 */
export function depthsOf(graph: Graph, back: ReadonlySet<string> = new Set()): Map<string, number> {
  const sources = new Map<string, string[]>();
  for (const wire of graph.wires.filter(
    (candidate) => !back.has(`${candidate.from}->${candidate.to}`),
  )) {
    const target = nodeOf(wire.to);
    sources.set(target, [...(sources.get(target) ?? []), nodeOf(wire.from)]);
  }

  const depths = new Map<string, number>();
  const depthOf = (id: string): number => {
    const known = depths.get(id);
    if (known !== undefined) {
      return known;
    }

    depths.set(id, 0);
    const depth = Math.max(-1, ...(sources.get(id) ?? []).map(depthOf)) + 1;
    depths.set(id, depth);
    return depth;
  };

  graph.nodes.forEach((node) => depthOf(node.id));
  return depths;
}
