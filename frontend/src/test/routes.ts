import { type FakeRoute, json } from './fakeApi';
import { catalog, h0001Graph } from './fixtures';

/** A check like the server's for the cases the tests need: an input of `compare` without a wire or a value. */
export function checkLikeServer(request: Request): Promise<Response> {
  return request.json().then((body: { graph: ReturnType<typeof h0001Graph> }) => {
    const graph = body.graph;
    const errors = graph.nodes.flatMap((node) => {
      if (node.kind === 'outputs') {
        return graph.outputs
          .filter((output) => !graph.wires.some((wire) => wire.to === `${node.id}.${output.name}`))
          .map((output) => ({
            code: 'MissingInput',
            reason: 'the output has no wire',
            node: node.id,
            pin: output.name,
          }));
      }

      if (node.kind !== 'compare') {
        return [];
      }

      return ['a', 'b']
        .filter(
          (pin) =>
            !graph.wires.some((wire) => wire.to === `${node.id}.${pin}`) &&
            node.inputs?.[pin] === undefined,
        )
        .map((pin) => ({
          code: 'MissingInput',
          reason: `the input ${pin} has no wire and no value`,
          node: node.id,
          pin,
        }));
    });
    return json({ errors });
  });
}

/** The routes every page of the constructor reads. */
export function baseRoutes(): Record<string, FakeRoute> {
  return {
    'GET /api/nodes': () => json(catalog),
    'GET /api/instruments': () =>
      json([
        { id: '1', symbol: 'BTCUSDT' },
        { id: '2', symbol: 'PRLUSDT' },
      ]),
    'GET /api/signals': () => json([]),
    // The header asks for the queue on every page under it.
    'GET /api/backtests': () => json([]),
    'POST /api/signals/validate': checkLikeServer,
  };
}

/** A saved signal `funding-high` with the graph of H0001. */
export function savedFundingHigh(layout: Record<string, { x: number; y: number }> = {}) {
  return { name: 'funding-high', description: null, version: 1, graph: h0001Graph(), layout };
}
