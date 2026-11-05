import { keepPreviousData, queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';

import { api, type Schemas, unwrap } from '@/shared/api/client';

export const savedFiltersQuery = queryOptions({
  queryKey: ['saved-filters'],
  queryFn: async () => unwrap(await api.GET('/api/saved-filters')),
});

/** The catalog of the fields a condition may test; it changes only with the backend. */
export const filterFieldsQuery = queryOptions({
  queryKey: ['saved-filter-fields'],
  queryFn: async () => unwrap(await api.GET('/api/saved-filters/fields')),
  staleTime: Infinity,
});

export type FilterBody = Schemas['SavedFilterBody'];

/** How many pairs and ideas meet the conditions of a draft of a filter; the API counts, nothing is saved. The old count stays while a new one loads. */
export function fitQuery(conditions: NonNullable<Schemas['SavedFilterFitBody']['conditions']>) {
  return queryOptions({
    queryKey: ['saved-filters', 'fit', conditions],
    queryFn: async () => unwrap(await api.POST('/api/saved-filters/fit', { body: { conditions } })),
    placeholderData: keepPreviousData,
  });
}

/** Creates a filter, or changes the one with `id`, and gives its id; the lists of filters are read again afterwards. */
export function useSaveFilter() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, body }: { id: string | null; body: FilterBody }): Promise<string> => {
      if (id === null) {
        return unwrap(await api.POST('/api/saved-filters', { body })).id;
      }

      unwrap(await api.PUT('/api/saved-filters/{id}', { params: { path: { id } }, body }));

      return id;
    },
    onSuccess: () => client.invalidateQueries({ queryKey: savedFiltersQuery.queryKey }),
  });
}

export function useDeleteFilter() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) =>
      unwrap(await api.DELETE('/api/saved-filters/{id}', { params: { path: { id } } })),
    onSuccess: () => client.invalidateQueries({ queryKey: savedFiltersQuery.queryKey }),
  });
}
