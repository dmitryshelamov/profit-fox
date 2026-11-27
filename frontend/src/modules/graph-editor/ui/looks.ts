import type { NodeKind, PinType } from '../model/types';

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
  order: {
    word: 'заявка',
    border: 'border-pin-order!',
    fill: 'bg-pin-order!',
    dot: 'bg-pin-order',
    stroke: 'var(--color-pin-order)',
  },
  position: {
    word: 'позиция',
    border: 'border-pin-position!',
    fill: 'bg-pin-position!',
    dot: 'bg-pin-position',
    stroke: 'var(--color-pin-position)',
  },
};

/** The types in the order of the legend. */
export const pinTypes: readonly PinType[] = [
  'number',
  'boolean',
  'duration',
  'event',
  'order',
  'position',
];

/**
 * The types the legend of an editor names (spec strategy-constructor «Типы заявки и позиции на холсте») by the
 * pins of its catalog: the four types of a value and an event always, an order and the position when the catalog has
 * them — four for a signal, six for a strategy.
 */
export function legendTypes(catalog: readonly NodeKind[]): PinType[] {
  const used = new Set(
    catalog.flatMap((kind) => [...kind.inputs, ...kind.outputs].map((pin) => pin.type)),
  );
  return pinTypes.filter((type) => (type !== 'order' && type !== 'position') || used.has(type));
}

/** The color of a wire with an error and of one whose type is not known. */
export const wireStrokes = { bad: 'var(--color-err)', unknown: 'var(--color-faint)' };

/** The groups of the catalog the theme has a color for. */
export type GroupKey =
  'graph' | 'data' | 'logic' | 'math' | 'window' | 'indicator' | 'position' | 'signal';

const groupKeys: Record<string, GroupKey> = {
  Граф: 'graph',
  Данные: 'data',
  Логика: 'logic',
  Числа: 'math',
  // Change add-setup-strategies: the windows over the last candles and the indicators.
  Окна: 'window',
  Индикаторы: 'indicator',
  Позиция: 'position',
  Сигналы: 'signal',
};

const groupFills: Record<GroupKey, string> = {
  graph: 'bg-group-graph',
  data: 'bg-group-data',
  logic: 'bg-group-logic',
  math: 'bg-group-math',
  window: 'bg-group-window',
  indicator: 'bg-group-indicator',
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
