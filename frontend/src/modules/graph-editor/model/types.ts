import type { Schemas } from '@/api/client';

/**
 * The type of a pin (spec strategy-graph «Типы пинов»): a value, an event, or, in a strategy, an order that goes into
 * the position and the position an exit node reads.
 */
export type PinType = 'number' | 'boolean' | 'duration' | 'event' | 'order' | 'position';

/** A written value: a number, a boolean, or a duration as the format writes it, `4h`. */
export type GraphValue = number | boolean | string;

export interface GraphParameter {
  readonly name: string;
  readonly type: PinType;
  readonly value: GraphValue;
  readonly minimum?: number;
  readonly maximum?: number;
}

export interface GraphOutput {
  readonly name: string;
  readonly type: PinType;
}

export interface GraphNode {
  readonly id: string;
  readonly kind: string;
  readonly settings?: Readonly<Record<string, string>>;
  readonly inputs?: Readonly<Record<string, GraphValue>>;
}

export interface GraphWire {
  readonly from: string;
  readonly to: string;
}

/** A graph in the format of spec strategy-graph, as the editor holds it. */
export interface Graph {
  readonly formatVersion: 1;
  readonly parameters: readonly GraphParameter[];
  readonly outputs: readonly GraphOutput[];
  readonly nodes: readonly GraphNode[];
  readonly wires: readonly GraphWire[];
}

export interface Position {
  readonly x: number;
  readonly y: number;
}

/** Where nodes stand on the canvas, by id; never part of the graph. */
export type Layout = Readonly<Record<string, Position>>;

/** What the editor edits: the graph and, beside it, the positions of its nodes. */
export interface GraphDocument {
  readonly graph: Graph;
  readonly layout: Layout;
}

export type NodeKind = Schemas['NodeKindResponse'];

export interface Pin {
  readonly name: string;
  readonly type: PinType;
  /** The value an input of a signal node takes without a wire and a value: that of the parameter of its version. */
  readonly fallback?: GraphValue;
  /** Whether the input takes several wires: the orders of the position (spec strategy-graph «Вход с несколькими проводами»). */
  readonly many?: boolean;
}

/** Whether a pin of the type takes a wire and never a written value. */
export function wireOnly(type: PinType): boolean {
  return type === 'event' || type === 'order' || type === 'position';
}

/** A parameter of a version of a saved signal, which is an input of a signal node on that version. */
export interface SignalParameter {
  readonly name: string;
  readonly type: PinType;
  readonly value: GraphValue;
  readonly minimum?: number | null;
  readonly maximum?: number | null;
}

/**
 * What the editor knows of a version of a saved signal (change add-strategy-constructor, design.md, decision 7): the
 * current name and latest version of the signal, and the parameters of the version — undefined while they load.
 */
export interface SignalVersionInfo {
  readonly name: string | null;
  readonly latestVersion: number | null;
  readonly parameters?: readonly SignalParameter[];
}

/**
 * The node catalog as the editor uses it. A strategy also carries what it knows of the versions of signals its signal
 * nodes point at, keyed by `<signal id>@<version>`: the pins of a signal node come from there.
 */
export type Catalog = readonly NodeKind[] & {
  readonly signalVersions?: ReadonlyMap<string, SignalVersionInfo>;
};
