import { queryOptions } from '@tanstack/react-query';

import { api, unwrap } from '@/shared/api/client';

export const strategiesQuery = queryOptions({
  queryKey: ['strategies'],
  queryFn: async () => unwrap(await api.GET('/api/strategies')),
});

export function strategyQuery(id: string) {
  return queryOptions({
    queryKey: ['strategies', id],
    queryFn: async () =>
      unwrap(await api.GET('/api/strategies/{id}', { params: { path: { id } } })),
  });
}

export async function deleteStrategy(id: string): Promise<void> {
  unwrap(await api.DELETE('/api/strategies/{id}', { params: { path: { id } } }));
}

/** Saves a new name with the definition as it is on the server: the API changes both at once. */
export async function renameStrategy(id: string, name: string): Promise<void> {
  const saved = unwrap(await api.GET('/api/strategies/{id}', { params: { path: { id } } }));

  unwrap(
    await api.PUT('/api/strategies/{id}', {
      params: { path: { id } },
      body: { name, definition: saved.definition },
    }),
  );
}

/** What saving the strategy would reject: a change of strategy `id`, or a new strategy without it. */
export async function validateStrategy(
  id: string | undefined,
  body: { name: string; definition: unknown },
  signal: AbortSignal,
) {
  return unwrap(
    id
      ? await api.POST('/api/strategies/{id}/validate', { params: { path: { id } }, body, signal })
      : await api.POST('/api/strategies/validate', { body, signal }),
  );
}

export async function saveStrategy(
  id: string,
  body: { name: string; definition: unknown },
): Promise<void> {
  unwrap(await api.PUT('/api/strategies/{id}', { params: { path: { id } }, body }));
}

export async function createStrategy(body: { name: string; definition: unknown }): Promise<string> {
  return unwrap(await api.POST('/api/strategies', { body })).id;
}
