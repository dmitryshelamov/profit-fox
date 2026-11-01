import { type PinContext, pinsOf } from './pins';
import type {
  AnyDef,
  CatalogNode,
  GraphNodeDef,
  NodeValue,
  ParameterDef,
  PinDecl,
  Position,
  WireDef,
} from './types';

/** The declarations of the definition that pins of `parameters`, `inputs` and `outputs` nodes follow. */
export function contextOf(definition: AnyDef): PinContext {
  return 'direction' in definition
    ? { parameters: definition.parameters }
    : { inputs: definition.inputs, outputs: definition.outputs };
}

/** Adds a node with the first free id `<kind>N`, the default settings of the catalog and a saved position. */
export function addNode<T extends AnyDef>(
  definition: T,
  catalog: readonly CatalogNode[],
  type: { kind: string; ref?: string },
  position: Position,
): { definition: T; id: string } {
  const ref = type.ref ?? '';
  const base = type.kind !== 'composite' ? type.kind : isCustom(ref) ? 'node' : ref;
  const taken = new Set(definition.nodes.map((node) => node.id));
  let number = 1;

  while (taken.has(`${base}${number}`)) {
    number++;
  }

  const id = `${base}${number}`;
  const settings = catalog.find((entry) => entry.id === type.kind)?.settings ?? [];
  const node: GraphNodeDef = {
    id,
    kind: type.kind,
    ...(type.ref !== undefined && { ref: type.ref }),
    ...(settings.length > 0 && {
      settings: Object.fromEntries(settings.map((setting) => [setting.name, setting.default])),
    }),
  };

  return {
    id,
    definition: moveNode({ ...definition, nodes: [...definition.nodes, node] }, id, position),
  };
}

export function moveNode<T extends AnyDef>(definition: T, id: string, position: Position): T {
  return moveNodes(definition, { [id]: position });
}

/** Saves the positions of nodes moved together, such as a dragged selection. */
export function moveNodes<T extends AnyDef>(
  definition: T,
  positions: Readonly<Record<string, Position>>,
): T {
  return { ...definition, layout: { ...definition.layout, ...positions } };
}

export function removeNode<T extends AnyDef>(definition: T, id: string): T {
  const { [id]: _, ...layout } = definition.layout ?? {};

  return {
    ...definition,
    nodes: definition.nodes.filter((node) => node.id !== id),
    wires: (definition.wires ?? []).filter(
      (wire) => nodeOf(wire.from) !== id && nodeOf(wire.to) !== id,
    ),
    layout,
  };
}

/** Wires `from` to the input `to`; an input has one wire, so its old wire and its value go away. */
export function connect<T extends AnyDef>(definition: T, from: string, to: string): T {
  const wires = (definition.wires ?? []).filter((wire) => wire.to !== to);
  const [nodeId = '', pin = ''] = to.split('.');

  return setInputValue({ ...definition, wires: [...wires, { from, to }] }, nodeId, pin, undefined);
}

export function disconnect<T extends AnyDef>(definition: T, from: string, to: string): T {
  return {
    ...definition,
    wires: (definition.wires ?? []).filter((wire) => wire.from !== from || wire.to !== to),
  };
}

/** Sets the value of an input, or clears it when `value` is `undefined`. */
export function setInputValue<T extends AnyDef>(
  definition: T,
  nodeId: string,
  pin: string,
  value: NodeValue | undefined,
): T {
  return updateNode(definition, nodeId, (node) => {
    const { [pin]: _, ...rest } = node.inputs ?? {};
    const inputs = value === undefined ? rest : { ...rest, [pin]: value };
    const { inputs: __, ...withoutInputs } = node;

    return Object.keys(inputs).length > 0 ? { ...withoutInputs, inputs } : withoutInputs;
  });
}

/** Changes a setting; wires its pins no longer accept go away. */
export function setSetting<T extends AnyDef>(
  definition: T,
  catalog: readonly CatalogNode[],
  nodeId: string,
  name: string,
  value: string,
): T {
  const changed = updateNode(definition, nodeId, (node) => ({
    ...node,
    settings: { ...node.settings, [name]: value },
  }));

  return pruneWires(changed, catalog);
}

/** Replaces the strategy parameters; wires from parameters that are gone or changed type go away. */
export function setParameters<T extends AnyDef>(
  definition: T,
  catalog: readonly CatalogNode[],
  parameters: readonly ParameterDef[],
): T {
  return pruneWires({ ...definition, parameters }, catalog);
}

/** Replaces the composite node declarations; wires to pins that are gone or changed type go away. */
export function setDeclarations<T extends AnyDef>(
  definition: T,
  catalog: readonly CatalogNode[],
  declarations: { inputs: readonly PinDecl[]; outputs: readonly PinDecl[] },
): T {
  return pruneWires({ ...definition, ...declarations }, catalog);
}

/** Renames a parameter or a declared input or output together with the wires of its pin. */
export function renamePin<T extends AnyDef>(
  definition: T,
  catalog: readonly CatalogNode[],
  declarations: 'parameters' | 'inputs' | 'outputs',
  from: string,
  to: string,
): T {
  const owners = new Set(
    definition.nodes.filter((node) => node.kind === declarations).map((node) => node.id),
  );
  const rename = (reference: string) =>
    owners.has(nodeOf(reference)) && reference.endsWith(`.${from}`)
      ? `${nodeOf(reference)}.${to}`
      : reference;
  const declared = (definition as unknown as Record<string, readonly { name: string }[]>)[
    declarations
  ]!;

  return pruneWires(
    {
      ...definition,
      [declarations]: declared.map((item) => (item.name === from ? { ...item, name: to } : item)),
      wires: (definition.wires ?? []).map((wire): WireDef => ({
        from: rename(wire.from),
        to: rename(wire.to),
      })),
    },
    catalog,
  );
}

function pruneWires<T extends AnyDef>(definition: T, catalog: readonly CatalogNode[]): T {
  const context = contextOf(definition);
  const typeOf = (reference: string, side: 'inputs' | 'outputs') => {
    const node = definition.nodes.find((candidate) => candidate.id === nodeOf(reference));
    const pin = reference.slice(reference.indexOf('.') + 1);

    return (
      node && pinsOf(node, catalog, context)[side].find((candidate) => candidate.name === pin)?.type
    );
  };

  return {
    ...definition,
    wires: (definition.wires ?? []).filter((wire) => {
      const output = typeOf(wire.from, 'outputs');

      return output !== undefined && output === typeOf(wire.to, 'inputs');
    }),
  };
}

function updateNode<T extends AnyDef>(
  definition: T,
  id: string,
  update: (node: GraphNodeDef) => GraphNodeDef,
): T {
  return {
    ...definition,
    nodes: definition.nodes.map((node) => (node.id === id ? update(node) : node)),
  };
}

function nodeOf(reference: string): string {
  return reference.slice(0, reference.indexOf('.'));
}

function isCustom(reference: string): boolean {
  return /^[0-9a-f]{8}-/.test(reference);
}
