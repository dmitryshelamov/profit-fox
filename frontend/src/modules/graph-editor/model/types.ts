import type { Schemas } from '@/api/client';

/** The type of a pin (spec strategy-graph «Типы пинов»). */
export type PinType = 'number' | 'boolean' | 'duration' | 'event';

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
}
