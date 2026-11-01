import { type PinContext, pinsOf } from './pins';
import type { CatalogNode, GraphDef } from './types';

/** Whether a wire may go from the output `from` to the input `to` (both written `node.pin`): their types must match. */
export function canConnect(
  definition: GraphDef,
  catalog: readonly CatalogNode[],
  context: PinContext,
  from: string,
  to: string,
): boolean {
  const output = pinType(definition, catalog, context, from, 'outputs');
  const input = pinType(definition, catalog, context, to, 'inputs');

  return output !== undefined && output === input;
}

function pinType(
  definition: GraphDef,
  catalog: readonly CatalogNode[],
  context: PinContext,
  reference: string,
  side: 'inputs' | 'outputs',
) {
  const [nodeId, pinName] = reference.split('.');
  const node = definition.nodes.find((candidate) => candidate.id === nodeId);

  return node && pinsOf(node, catalog, context)[side].find((pin) => pin.name === pinName)?.type;
}
