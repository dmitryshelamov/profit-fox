/** A response handler of the fake API, keyed by "METHOD /path". */
export type FakeRoute = (request: Request) => Response | Promise<Response>;

export function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

/**
 * Replaces the global fetch with a routing table. A request no route takes answers 404, so a test sees exactly the
 * calls it expects. Returns the recorded requests.
 */
export function fakeApi(routes: Record<string, FakeRoute>): Request[] {
  const requests: Request[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const request =
        input instanceof Request
          ? input
          : new Request(new URL(String(input), location.origin), init);
      const url = new URL(request.url);
      requests.push(request.clone());
      const route = routes[`${request.method} ${url.pathname}`];
      return route
        ? route(request)
        : json({ status: 404, detail: `not routed: ${request.method} ${url.pathname}` }, 404);
    }),
  );
  return requests;
}

/** The JSON bodies of the recorded requests to one route. */
export async function bodiesOf(
  requests: readonly Request[],
  method: string,
  path: string,
): Promise<unknown[]> {
  const matching = requests.filter(
    (request) => request.method === method && new URL(request.url).pathname === path,
  );
  return Promise.all(matching.map((request) => request.clone().json()));
}
