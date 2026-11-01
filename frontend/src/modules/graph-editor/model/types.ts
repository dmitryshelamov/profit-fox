/** The graph format of strategies and composite nodes, as `StrategyJson` reads and writes it. */
export type PinType = 'number' | 'boolean' | 'duration';

/** A number, `true`/`false`, or a duration such as `01:00:00`. */
export type NodeValue = number | boolean | string;

export interface Position {
  readonly x: number;
  readonly y: number;
}

export interface GraphNodeDef {
  readonly id: string;
  /** A simple node kind such as `ema`, or `composite` with {@link ref}. */
  readonly kind: string;
  readonly ref?: string;
  readonly settings?: Readonly<Record<string, string>>;
  readonly inputs?: Readonly<Record<string, NodeValue>>;
}

export interface WireDef {
  /** `node.pin` of an output. */
  readonly from: string;
  /** `node.pin` of an input. */
  readonly to: string;
}

export interface ParameterDef {
  readonly name: string;
  readonly type: PinType;
  readonly default?: NodeValue;
  readonly min?: NodeValue;
  readonly max?: NodeValue;
  readonly integer?: boolean;
}

export interface PinDecl {
  readonly name: string;
  readonly type: PinType;
}

export interface GraphDef {
  readonly formatVersion: number;
  readonly nodes: readonly GraphNodeDef[];
  readonly wires?: readonly WireDef[];
  readonly layout?: Readonly<Record<string, Position>>;
}

export interface StrategyDef extends GraphDef {
  readonly direction: 'long' | 'short';
  readonly parameters?: readonly ParameterDef[];
}

export interface CompositeDef extends GraphDef {
  readonly inputs: readonly PinDecl[];
  readonly outputs: readonly PinDecl[];
}

export type AnyDef = StrategyDef | CompositeDef;

/** A node of the library catalog (`GET /api/strategy-nodes`). */
export interface CatalogNode {
  readonly id: string;
  readonly title: string;
  readonly group: string;
  readonly inputs: readonly { readonly name: string; readonly type: string }[];
  readonly outputs: readonly { readonly name: string; readonly type: string }[];
  readonly settings: readonly CatalogSetting[];
}

export interface CatalogSetting {
  readonly name: string;
  readonly values: readonly string[];
  readonly default: string;
  readonly inputs: readonly string[];
}

export interface Pin {
  readonly name: string;
  readonly type: PinType;
}
