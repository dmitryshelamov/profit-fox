import { nodeOf, pinOf } from './graph';
import { freePlace, pinPlaces } from './operations';
import type {
  Catalog,
  GraphDocument,
  GraphNode,
  NodeKind,
  Position,
  SignalParameter,
  SignalVersionInfo,
} from './types';

/** A saved signal a strategy may add as a node: its id, its name and its latest version. */
export interface SignalEntry {
  readonly id: string;
  readonly name: string;
  readonly version: number;
}

/** What a signal node of a strategy points at, as the check and the read of a strategy name it. */
export interface SignalUse {
  readonly node: string;
  readonly signal: string;
  readonly name?: string | null;
  readonly version: number | string;
  readonly latestVersion?: number | string | null;
  readonly parameters: readonly {
    readonly name: string;
    readonly type: string;
    readonly value: unknown;
    readonly minimum?: number | string | null;
    readonly maximum?: number | string | null;
  }[];
}

/** The key of a version of a signal: `<signal id>@<version>`. */
export function versionKey(signal: string, version: number | string): string {
  return `${signal}@${version}`;
}

/** The reference a signal node holds, or undefined for another node or a reference not written. */
export function referenceOf(node: GraphNode): { signal: string; version: number } | undefined {
  const signal = node.settings?.signal;
  const version = Number(node.settings?.version);
  return node.kind === 'signal' && signal !== undefined && Number.isInteger(version)
    ? { signal, version }
    : undefined;
}

/** What the editor knows of the version a signal node points at. */
export function signalInfo(node: GraphNode, catalog: Catalog): SignalVersionInfo | undefined {
  const reference = referenceOf(node);
  return reference && catalog.signalVersions?.get(versionKey(reference.signal, reference.version));
}

/** The catalog carrying what is known of the versions of signals. */
export function withSignalVersions(
  catalog: readonly NodeKind[],
  versions: ReadonlyMap<string, SignalVersionInfo>,
): Catalog {
  return Object.assign([...catalog], { signalVersions: versions });
}

/**
 * The known versions with what an answer of the API says of the signal nodes: the server's word on the parameters of
 * a version is final, since a version never changes; the name and the latest version are those of now.
 */
export function mergeUses(
  versions: ReadonlyMap<string, SignalVersionInfo>,
  uses: readonly SignalUse[],
): Map<string, SignalVersionInfo> {
  const merged = new Map(versions);
  for (const use of uses) {
    merged.set(versionKey(use.signal, use.version), {
      name: use.name ?? null,
      latestVersion: numberOrNull(use.latestVersion),
      parameters: use.parameters.map((parameter) => ({
        name: parameter.name,
        type: parameter.type as SignalParameter['type'],
        value: parameter.value as SignalParameter['value'],
        minimum: numberOrNull(parameter.minimum),
        maximum: numberOrNull(parameter.maximum),
      })),
    });
  }

  return merged;
}

function numberOrNull(value: number | string | null | undefined): number | null {
  return value === undefined || value === null ? null : Number(value);
}

/** A free id for the node of a signal: its name, or its name with a number. */
function signalNodeId(document: GraphDocument, name: string): string {
  const taken = new Set(document.graph.nodes.map((node) => node.id));
  if (!taken.has(name)) {
    return name;
  }

  for (let number = 1; ; number += 1) {
    if (!taken.has(`${name}-${number}`)) {
      return `${name}-${number}`;
    }
  }
}

/** Adds a node of a saved signal on its latest version (spec strategy-constructor «Добавление сигнала»). */
export function addSignalNode(
  document: GraphDocument,
  catalog: Catalog,
  signal: SignalEntry,
  position: Position,
): { document: GraphDocument; id: string } {
  const pinned = pinPlaces(document, catalog);
  const id = signalNodeId(pinned, signal.name);
  const node: GraphNode = {
    id,
    kind: 'signal',
    settings: { signal: signal.id, version: String(signal.version) },
  };
  return {
    id,
    document: {
      graph: { ...pinned.graph, nodes: [...pinned.graph.nodes, node] },
      layout: { ...pinned.layout, [id]: freePlace(pinned.layout, position) },
    },
  };
}

/**
 * Points a signal node at another version (spec strategy-constructor «Обновление версии сигнала в узле»): the values
 * and wires of the inputs the version has stay, those of other inputs go.
 */
export function updateSignalVersion(
  document: GraphDocument,
  id: string,
  version: number,
  parameters: readonly string[],
): GraphDocument {
  const kept = new Set(parameters);
  return {
    ...document,
    graph: {
      ...document.graph,
      nodes: document.graph.nodes.map((node) => {
        if (node.id !== id) {
          return node;
        }

        const inputs = Object.fromEntries(
          Object.entries(node.inputs ?? {}).filter(([name]) => kept.has(name)),
        );
        const updated: GraphNode = {
          ...node,
          settings: { ...node.settings, version: String(version) },
        };
        if (Object.keys(inputs).length > 0) {
          return { ...updated, inputs };
        }

        const { inputs: _dropped, ...rest } = updated;
        void _dropped;
        return rest;
      }),
      wires: document.graph.wires.filter(
        (wire) => nodeOf(wire.to) !== id || kept.has(pinOf(wire.to)),
      ),
    },
  };
}

/**
 * The draft of a new strategy from a saved signal (spec strategy-constructor «Стратегия из сигнала»): the node of the
 * signal on its latest version, an `enter` whose `when` takes its event and a `close-after`; no side and no time.
 */
export function strategyOfSignal(signal: SignalEntry): GraphDocument {
  return {
    graph: {
      formatVersion: 1,
      parameters: [],
      outputs: [],
      nodes: [
        {
          id: signal.name,
          kind: 'signal',
          settings: { signal: signal.id, version: String(signal.version) },
        },
        { id: 'enter', kind: 'enter' },
        { id: 'close-after', kind: 'close-after' },
      ],
      wires: [{ from: `${signal.name}.event`, to: 'enter.when' }],
    },
    layout: {},
  };
}
