/** A response handler of the fake API, keyed by "METHOD /path?query". */
export type FakeRoute = (request: Request) => Response | Promise<Response>;

export function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

export function problem(status: number, detail: string, extensions: object = {}): Response {
  return new Response(JSON.stringify({ status, detail, ...extensions }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });
}

/**
 * Replaces global fetch with a routing table. Unrouted requests answer 404, so a test sees exactly the calls it expects.
 * Returns the recorded requests.
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
      const route =
        routes[`${request.method} ${url.pathname}${url.search}`] ??
        routes[`${request.method} ${url.pathname}`];

      return route ? route(request) : problem(404, `not routed: ${request.method} ${url.pathname}`);
    }),
  );

  return requests;
}
