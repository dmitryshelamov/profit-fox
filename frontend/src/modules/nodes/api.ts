import { queryOptions } from '@tanstack/react-query';

import { api, unwrap } from '@/shared/api/client';

export const catalogQuery = queryOptions({
  queryKey: ['strategy-nodes'],
  queryFn: async () => unwrap(await api.GET('/api/strategy-nodes')),
});

export async function deleteNode(id: string): Promise<void> {
  unwrap(await api.DELETE('/api/strategy-nodes/{id}', { params: { path: { id } } }));
}

export async function cloneNode(id: string): Promise<string> {
  return unwrap(await api.POST('/api/strategy-nodes/{id}/clone', { params: { path: { id } } })).id;
}

export function nodeQuery(id: string) {
  return queryOptions({
    queryKey: ['strategy-nodes', id],
    queryFn: async () =>
      unwrap(await api.GET('/api/strategy-nodes/{id}', { params: { path: { id } } })),
  });
}

export async function validateNode(
  id: string,
  body: { name: string; definition: unknown },
  signal: AbortSignal,
) {
  return unwrap(
    await api.POST('/api/strategy-nodes/{id}/validate', { params: { path: { id } }, body, signal }),
  );
}

export async function saveNode(
  id: string,
  body: { name: string; definition: unknown },
): Promise<void> {
  unwrap(await api.PUT('/api/strategy-nodes/{id}', { params: { path: { id } }, body }));
}
