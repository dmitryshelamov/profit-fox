import type { PinType } from '../model/types';

/**
 * The colors of the canvas in one table (change redesign-bench-ui, design.md, decision 8): a pin and the wire from it
 * go by the type of the value, the header of a node by its group in the catalog. The classes name tokens of the theme;
 * `stroke` is the same token for the wire of React Flow, which takes its color as a style, not a class.
 */
export interface PinLook {
  /** The type in the words of the legend. */
  readonly word: string;
  /** The outline of a pin and the fill of a connected one; they override the look React Flow gives a handle. */
  readonly border: string;
  readonly fill: string;
  /** The dot of the type in the legend and in a card. */
  readonly dot: string;
  readonly stroke: string;
}

export const pinLooks: Record<PinType, PinLook> = {
  number: {
    word: 'число',
    border: 'border-pin-number!',
    fill: 'bg-pin-number!',
    dot: 'bg-pin-number',
    stroke: 'var(--color-pin-number)',
  },
  boolean: {
    word: 'логическое',
    border: 'border-pin-boolean!',
    fill: 'bg-pin-boolean!',
    dot: 'bg-pin-boolean',
    stroke: 'var(--color-pin-boolean)',
  },
  duration: {
    word: 'длительность',
    border: 'border-pin-duration!',
    fill: 'bg-pin-duration!',
    dot: 'bg-pin-duration',
    stroke: 'var(--color-pin-duration)',
  },
  event: {
    word: 'событие',
    border: 'border-pin-event!',
    fill: 'bg-pin-event!',
    dot: 'bg-pin-event',
    stroke: 'var(--color-pin-event)',
  },
};

/** The types in the order of the legend. */
export const pinTypes: readonly PinType[] = ['number', 'boolean', 'duration', 'event'];

/** The color of a wire with an error and of one whose type is not known. */
export const wireStrokes = { bad: 'var(--color-err)', unknown: 'var(--color-faint)' };

/** The groups of the catalog the theme has a color for. */
export type GroupKey = 'graph' | 'data' | 'logic' | 'position' | 'signal';

const groupKeys: Record<string, GroupKey> = {
  Граф: 'graph',
  Данные: 'data',
  Логика: 'logic',
  Позиция: 'position',
  Сигналы: 'signal',
};

const groupFills: Record<GroupKey, string> = {
  graph: 'bg-group-graph',
  data: 'bg-group-data',
  logic: 'bg-group-logic',
  position: 'bg-group-position',
  signal: 'bg-group-signal',
};

/** The group of a node by the group of its kind in the catalog; a signal node is of the signals, an unknown group of the graph. */
export function groupOf(kind: string | undefined, group: string | undefined): GroupKey {
  return kind === 'signal'
    ? 'signal'
    : ((group === undefined ? undefined : groupKeys[group]) ?? 'graph');
}

/** The fill of the header of a node of a group, and of its square in the palette. */
export function groupFill(group: GroupKey): string {
  return groupFills[group];
}
