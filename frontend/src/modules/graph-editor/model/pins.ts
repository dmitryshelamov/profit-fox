import type { CatalogNode, GraphNodeDef, ParameterDef, Pin, PinDecl, PinType } from './types';

/** What the pins of `parameters`, `inputs` and `outputs` nodes come from: the strategy or composite node being edited. */
export interface PinContext {
  readonly parameters?: readonly ParameterDef[];
  readonly inputs?: readonly PinDecl[];
  readonly outputs?: readonly PinDecl[];
}

export interface NodePins {
  readonly inputs: readonly Pin[];
  readonly outputs: readonly Pin[];
}

function catalogEntry(
  node: GraphNodeDef,
  catalog: readonly CatalogNode[],
): CatalogNode | undefined {
  const id = node.kind === 'composite' ? node.ref : node.kind;

  return catalog.find((entry) => entry.id === id);
}

/** The pins of a node as the canvas shows them. */
export function pinsOf(
  node: GraphNodeDef,
  catalog: readonly CatalogNode[],
  context: PinContext,
): NodePins {
  switch (node.kind) {
    case 'parameters':
      return { inputs: [], outputs: pins(context.parameters) };
    case 'inputs':
      return { inputs: [], outputs: pins(context.inputs) };
    case 'outputs':
      return { inputs: pins(context.outputs), outputs: [] };
  }

  const entry = catalogEntry(node, catalog);

  if (!entry) {
    return { inputs: [], outputs: [] };
  }

  const typed = new Map(
    entry.settings.flatMap((setting) =>
      setting.inputs.map((input) => [input, node.settings?.[setting.name] ?? setting.default]),
    ),
  );

  return {
    inputs: entry.inputs.map((pin) => ({
      name: pin.name,
      type: (typed.get(pin.name) ?? pin.type) as PinType,
    })),
    outputs: entry.outputs.map((pin) => ({ name: pin.name, type: pin.type as PinType })),
  };
}

function pins(declared: readonly PinDecl[] = []): Pin[] {
  return declared.map((pin) => ({ name: pin.name, type: pin.type }));
}
