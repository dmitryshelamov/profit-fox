import type { AnyDef } from './types';

export interface Edited {
  readonly name: string;
  readonly definition: AnyDef;
}

export interface Difference {
  /** Nothing that affects the strategy or node differs; positions on the canvas are not compared. */
  readonly same: boolean;
  readonly nodesAdded: number;
  readonly nodesRemoved: number;
  readonly wiresAdded: number;
  readonly wiresRemoved: number;
}

export function compareDrafts(saved: Edited, draft: Edited): Difference {
  const nodes = (edited: Edited) => edited.definition.nodes.map((node) => node.id);
  const wires = (edited: Edited) =>
    (edited.definition.wires ?? []).map((wire) => `${wire.from}->${wire.to}`);

  return {
    same: saved.name === draft.name && withoutLayout(saved) === withoutLayout(draft),
    nodesAdded: missing(nodes(draft), nodes(saved)),
    nodesRemoved: missing(nodes(saved), nodes(draft)),
    wiresAdded: missing(wires(draft), wires(saved)),
    wiresRemoved: missing(wires(saved), wires(draft)),
  };
}

function withoutLayout({ definition }: Edited): string {
  const { layout: _, ...rest } = definition;

  return canonical(rest);
}

/** JSON with the keys of every object sorted: equal values give equal text whatever order their keys were set in. */
function canonical(value: unknown): string {
  return JSON.stringify(value, (_, item: unknown) =>
    item !== null && typeof item === 'object' && !Array.isArray(item)
      ? Object.fromEntries(Object.entries(item).sort(([one], [other]) => one.localeCompare(other)))
      : item,
  );
}

/** How many of `items` are not among `others`. */
function missing(items: readonly string[], others: readonly string[]): number {
  return items.filter((item) => !others.includes(item)).length;
}

/** Whether two edits are the same, positions on the canvas included: moving a node is an edit to save. */
export function sameEdit(one: Edited, other: Edited): boolean {
  return one.name === other.name && canonical(one.definition) === canonical(other.definition);
}
