import type { NodeKind } from '@/modules/graph-editor/model/types';

import { type FakeRoute, json } from './fakeApi';
import { h0001Graph } from './fixtures';
import strategyCatalogFile from './strategyCatalog.fixture.json';

/** The node catalog of a strategy as the bench API serves it (`GET /api/nodes/strategy`); the JSON leaves out nulls. */
export const strategyCatalog = strategyCatalogFile as unknown as NodeKind[];

/** The id of the saved signal `funding-high`. */
export const fundingHighId = '0b6f8d4e-3c2a-4f51-9d7e-2a1b3c4d5e6f';

/** A saved signal of the tests: its name, latest version and the parameters of each version. */
export interface TestSignal {
  readonly id: string;
  readonly name: string;
  readonly latest: number;
  readonly versions: Record<number, readonly { name: string; type: string; value: unknown }[]>;
}

/** `funding-high` with the graph of H0001: version 1 with the threshold 0.0005, version 2 with 0.001. */
export const fundingHigh: TestSignal = {
  id: fundingHighId,
  name: 'funding-high',
  latest: 2,
  versions: {
    1: [{ name: 'threshold', type: 'number', value: 0.0005 }],
    2: [{ name: 'threshold', type: 'number', value: 0.001 }],
  },
};

interface Node {
  id: string;
  kind: string;
  settings?: Record<string, string | undefined>;
  inputs?: Record<string, unknown>;
}

/** What the signal nodes of a graph point at, as the API names it. */
export function usesOf(nodes: readonly Node[], signals: readonly TestSignal[]) {
  return nodes
    .filter((node) => node.kind === 'signal')
    .map((node) => {
      const signal = signals.find((candidate) => candidate.id === node.settings?.signal);
      const version = Number(node.settings?.version);
      return {
        node: node.id,
        signal: node.settings?.signal ?? '',
        name: signal?.name ?? null,
        version,
        latestVersion: signal?.latest ?? null,
        parameters: (signal?.versions[version] ?? []).map((parameter) => ({
          ...parameter,
          minimum: null,
          maximum: null,
        })),
      };
    });
}

/**
 * A check of a strategy like the server's for what the tests need: a side of `enter`, the inputs of `close-after` and
 * `stop-loss`, the event of `enter`, and what each signal node points at.
 */
export function checkStrategyLikeServer(signals: readonly TestSignal[]): FakeRoute {
  return async (request) => {
    const { graph } = (await request.json()) as {
      graph: { nodes: Node[]; wires: { from: string; to: string }[] };
    };
    const wired = (to: string) => graph.wires.some((wire) => wire.to === to);
    const errors = graph.nodes.flatMap((node) => {
      if (node.kind === 'enter') {
        return [
          ...(node.settings?.side === undefined
            ? [
                {
                  code: 'MissingSetting',
                  reason: 'The setting side is required.',
                  node: node.id,
                  setting: 'side',
                },
              ]
            : []),
          ...(!wired(`${node.id}.when`)
            ? [
                {
                  code: 'MissingInput',
                  reason: 'The input needs a wire.',
                  node: node.id,
                  pin: 'when',
                },
              ]
            : []),
        ];
      }

      const input =
        node.kind === 'close-after' ? 'after' : node.kind === 'stop-loss' ? 'distance' : null;
      return input !== null && !wired(`${node.id}.${input}`) && node.inputs?.[input] === undefined
        ? [
            {
              code: 'MissingInput',
              reason: 'The input needs a wire or a value.',
              node: node.id,
              pin: input,
            },
          ]
        : [];
    });
    return json({ errors, signals: usesOf(graph.nodes, signals) });
  };
}

/** A saved signal as `GET /api/signals/<name>` gives it, on its latest version. */
export function signalResponse(signal: TestSignal) {
  const graph = h0001Graph();
  return {
    id: signal.id,
    name: signal.name,
    description: null,
    version: signal.latest,
    graph: {
      ...graph,
      parameters: (signal.versions[signal.latest] ?? []).map((parameter) => ({ ...parameter })),
    },
    layout: {},
  };
}

/** The routes of the section «Стратегии» over saved signals. */
export function strategyRoutes(
  signals: readonly TestSignal[] = [fundingHigh],
): Record<string, FakeRoute> {
  return {
    'GET /api/nodes/strategy': () => json(strategyCatalog),
    'GET /api/strategies': () => json([]),
    'POST /api/strategies/validate': checkStrategyLikeServer(signals),
    'GET /api/signals': () =>
      json(
        signals.map((signal) => ({
          name: signal.name,
          description: null,
          version: signal.latest,
          error: null,
          id: signal.id,
        })),
      ),
    ...Object.fromEntries(
      signals.map(
        (signal) =>
          [`GET /api/signals/${signal.name}`, () => json(signalResponse(signal))] as const,
      ),
    ),
  };
}

/** A graph of a strategy as the tests write it. */
export interface TestGraph {
  readonly formatVersion: 1;
  readonly parameters: readonly unknown[];
  readonly outputs: readonly unknown[];
  readonly nodes: readonly Node[];
  readonly wires: readonly { from: string; to: string }[];
}

/** The strategy `funding-short` as written: the node `s` on <paramref name="version" /> of `funding-high`. */
export function fundingShort(version = 1): TestGraph {
  return {
    formatVersion: 1,
    parameters: [],
    outputs: [],
    nodes: [
      { id: 's', kind: 'signal', settings: { signal: fundingHighId, version: String(version) } },
      { id: 'enter', kind: 'enter', settings: { side: 'short' } },
      { id: 'close-after', kind: 'close-after', inputs: { after: '4h' } },
    ],
    wires: [{ from: 's.event', to: 'enter.when' }],
  };
}

/** The answer of `GET /api/strategies/funding-short`. */
export function savedFundingShort(
  version = 1,
  graph: ReturnType<typeof fundingShort> = fundingShort(version),
) {
  return {
    name: 'funding-short',
    description: null,
    version: 1,
    graph,
    layout: {},
    signals: usesOf(graph.nodes, [fundingHigh]),
  };
}
