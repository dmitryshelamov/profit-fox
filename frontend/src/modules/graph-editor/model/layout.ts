import { contextOf } from './operations';
import { type PinContext, pinsOf } from './pins';
import type { AnyDef, CatalogNode, GraphNodeDef, Position } from './types';

/** The width of a node on the canvas: fixed, so the layout knows it without measuring. */
export const nodeWidth = 200;

const columnGap = 80;
// Heights of the parts of a node as `GraphNodeView` draws them, with room for the thicker outline of a selected node.
const headerHeight = 31;
const settingsHeight = 25;
const pinsPadding = 8;
const pinRowHeight = 22;
const outlineHeight = 4;
const rowGap = 40;
const laneGap = 80;

/**
 * Places every node by stages, left to right: the column of a node is its depth, the longest path of wires to it from
 * the nodes without incoming wires. Lanes go top to bottom: the lane of a node is the first decision of the graph, a
 * node without outgoing wires, that it leads to; nodes leading to no decision come last.
 */
export function layoutGraph(
  definition: AnyDef,
  catalog: readonly CatalogNode[],
): Record<string, Position> {
  const context = contextOf(definition);
  const depths = depthsOf(definition);
  const lanes = lanesOf(definition);
  // The nodes of each cell "lane × column" stand one under another in the order of the definition.
  const offsets = new Map<string, number>();
  const filled = new Map<string, number>();
  const laneHeights = new Map<number, number>();

  for (const node of definition.nodes) {
    const lane = lanes.get(node.id)!;
    const cell = `${lane}:${depths.get(node.id)}`;
    const offset = filled.get(cell) ?? 0;
    const bottom = offset + nodeSize(node, catalog, context).height;
    offsets.set(node.id, offset);
    filled.set(cell, bottom + rowGap);
    laneHeights.set(lane, Math.max(laneHeights.get(lane) ?? 0, bottom));
  }

  const laneTops = new Map<number, number>();
  let top = 0;

  for (const lane of [...laneHeights.keys()].sort((a, b) => a - b)) {
    laneTops.set(lane, top);
    top += laneHeights.get(lane)! + laneGap;
  }

  return Object.fromEntries(
    definition.nodes.map((node) => [
      node.id,
      {
        x: depths.get(node.id)! * (nodeWidth + columnGap),
        y: laneTops.get(lanes.get(node.id)!)! + offsets.get(node.id)!,
      },
    ]),
  );
}

function nodeOf(reference: string): string {
  return reference.slice(0, reference.indexOf('.'));
}

function depthsOf(definition: AnyDef): Map<string, number> {
  const sources = new Map<string, string[]>();

  for (const wire of definition.wires ?? []) {
    const target = nodeOf(wire.to);
    sources.set(target, [...(sources.get(target) ?? []), nodeOf(wire.from)]);
  }

  const depths = new Map<string, number>();
  const depthOf = (id: string): number => {
    const known = depths.get(id);

    if (known !== undefined) {
      return known;
    }

    // A wire back into the path being walked closes a cycle of an invalid graph: it does not add depth.
    depths.set(id, 0);
    const depth = Math.max(-1, ...(sources.get(id) ?? []).map(depthOf)) + 1;
    depths.set(id, depth);

    return depth;
  };

  definition.nodes.forEach((node) => depthOf(node.id));

  return depths;
}

function lanesOf(definition: AnyDef): Map<string, number> {
  const targets = new Map<string, string[]>();

  for (const wire of definition.wires ?? []) {
    const source = nodeOf(wire.from);
    targets.set(source, [...(targets.get(source) ?? []), nodeOf(wire.to)]);
  }

  const decisions = definition.nodes.filter((node) => !targets.has(node.id)).map((node) => node.id);
  const lanes = new Map<string, number>();

  for (const node of definition.nodes) {
    const reached = new Set([node.id]);
    const queue = [node.id];

    for (let next = queue.shift(); next !== undefined; next = queue.shift()) {
      for (const target of targets.get(next) ?? []) {
        if (!reached.has(target)) {
          reached.add(target);
          queue.push(target);
        }
      }
    }

    const lane = decisions.findIndex((decision) => reached.has(decision));
    lanes.set(node.id, lane === -1 ? decisions.length : lane);
  }

  return lanes;
}

export function nodeSize(
  node: GraphNodeDef,
  catalog: readonly CatalogNode[],
  context: PinContext,
): { width: number; height: number } {
  const pins = pinsOf(node, catalog, context);
  const rows = Math.max(pins.inputs.length, pins.outputs.length);
  const settings = Object.keys(node.settings ?? {}).length > 0 ? settingsHeight : 0;

  return {
    width: nodeWidth,
    height: headerHeight + settings + pinsPadding + rows * pinRowHeight + outlineHeight,
  };
}

/**
 * The edit with every node pinned where it stood on the canvas before it: a node that has no saved position gets the
 * one of the layout by stages before the edit. Nodes the edit removed or brought without a position are left out.
 */
export function keepPlaces<T extends AnyDef>(
  before: AnyDef,
  after: T,
  catalog: readonly CatalogNode[],
): T {
  const shown = { ...layoutGraph(before, catalog), ...before.layout };
  const layout = { ...after.layout };

  for (const node of after.nodes) {
    const place = shown[node.id];

    if (!layout[node.id] && place) {
      layout[node.id] = place;
    }
  }

  return { ...after, layout };
}
