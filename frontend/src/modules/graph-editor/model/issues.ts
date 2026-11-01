import type { AnyDef, StrategyDef } from './types';

/** One error of a check or a rejected save, with what it belongs to on the screen. */
export interface Issue {
  readonly key: string;
  readonly message: string;
  /** The node of the edited graph, also for an error deeper inside a composite node it stands for. */
  readonly nodeId?: string;
  readonly pin?: string;
  readonly parameter?: string;
  readonly field?: string;
}

/**
 * Places errors keyed by path: `graph.enter.when` (node and pin), `graph.ladder1/mul2.b` (inside a composite node),
 * `graph.fast` (a node or a parameter of that name), `definition.nodes[2].kind` (reading the definition), `name`.
 */
export function issuesOf(
  errors: Readonly<Record<string, readonly string[]>>,
  definition: AnyDef,
): Issue[] {
  return Object.entries(errors).flatMap(([key, messages]) =>
    messages.map((message) => ({ key, message, ...targetOf(key, definition) })),
  );
}

function targetOf(key: string, definition: AnyDef): Omit<Issue, 'key' | 'message'> {
  const read = /^definition\.(parameters|nodes)\[(\d+)\]/.exec(key);

  if (read) {
    const index = Number(read[2]);

    return read[1] === 'nodes'
      ? { nodeId: definition.nodes[index]?.id }
      : { parameter: (definition as StrategyDef).parameters?.[index]?.name };
  }

  if (!key.startsWith('graph.')) {
    return key === 'graph' ? {} : { field: key };
  }

  const [path = '', pin] = key.slice('graph.'.length).split('.');
  const nodeId = path.split('/')[0]!;

  if (!definition.nodes.some((node) => node.id === nodeId)) {
    return { parameter: path };
  }

  return path.includes('/') || pin === undefined ? { nodeId } : { nodeId, pin };
}
