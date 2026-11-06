import { keepPreviousData, queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';

import { api, type Schemas, unwrap } from '@/shared/api/client';

import { type AutoFormValues, bodyOfForm } from './model/autoForm';

/** The autos in the order of the list with their totals, read again every 15 seconds. */
export function autosQuery() {
  return queryOptions({
    queryKey: ['idea-autos', 'list'],
    queryFn: async () => unwrap(await api.GET('/api/idea-autos')),
    refetchInterval: 15_000,
  });
}

export function autoQuery(id: string) {
  return queryOptions({
    queryKey: ['idea-autos', 'one', id],
    queryFn: async () =>
      unwrap(await api.GET('/api/idea-autos/{id}', { params: { path: { id } } })),
  });
}

/** The state of the guards of the money, read again every 15 seconds; saving the settings reads it at once. */
export function guardsQuery() {
  return queryOptions({
    queryKey: ['idea-trades', 'guards'],
    queryFn: async () => unwrap(await api.GET('/api/idea-trading/guards')),
    refetchInterval: 15_000,
  });
}

/** What the refusals are asked for: `today` or `7d`, one auto or all (an empty text), the page and its size. */
export interface RefusalsRequest {
  readonly period: string;
  readonly autoId: string;
  readonly page: number;
  readonly pageSize: number;
}

/** A page of the refusals of the autos from the newest; the old page stays while a new one loads. */
export function refusalsQuery(request: RefusalsRequest) {
  return queryOptions({
    queryKey: ['idea-autos', 'refusals', request],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/idea-autos/refusals', {
          params: {
            query: {
              period: request.period,
              autoId: request.autoId === '' ? undefined : request.autoId,
              page: request.page,
              pageSize: request.pageSize,
            },
          },
        }),
      ),
    placeholderData: keepPreviousData,
    refetchInterval: 15_000,
  });
}

export async function setAutoMode(id: string, mode: string): Promise<void> {
  unwrap(await api.PUT('/api/idea-autos/{id}/mode', { params: { path: { id } }, body: { mode } }));
}

export async function setAutosOrder(ids: readonly string[]): Promise<void> {
  unwrap(await api.PUT('/api/idea-autos/order', { body: { ids: [...ids] } }));
}

export async function deleteAuto(id: string): Promise<void> {
  unwrap(await api.DELETE('/api/idea-autos/{id}', { params: { path: { id } } }));
}

export interface SaveAutoInput {
  /** The auto saved before; none for a new one. */
  readonly id: string | null;
  readonly form: AutoFormValues;
  /** The mode the auto has now (`off` for a new one): it is changed only when the form holds another. */
  readonly savedMode: string;
}

/** Saves the auto, then its mode when it changed; gives the id of the auto. */
export async function saveAuto({ id, form, savedMode }: SaveAutoInput): Promise<string> {
  const body = bodyOfForm(form);
  let savedId: string;

  if (id === null) {
    savedId = unwrap(await api.POST('/api/idea-autos', { body })).id;
  } else {
    unwrap(await api.PUT('/api/idea-autos/{id}', { params: { path: { id } }, body }));
    savedId = id;
  }

  if (form.mode !== savedMode) {
    await setAutoMode(savedId, form.mode);
  }

  return savedId;
}

export function useSaveAuto() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: saveAuto,
    onSettled: () => client.invalidateQueries({ queryKey: ['idea-autos'] }),
  });
}

export function useSetAutoMode() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: ({ id, mode }: { id: string; mode: string }) => setAutoMode(id, mode),
    onSettled: () => client.invalidateQueries({ queryKey: ['idea-autos'] }),
  });
}

export function useSetAutosOrder() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: setAutosOrder,
    // The rows stand in the new order at once; a failed save is put right by the read that follows.
    onMutate: (ids) =>
      client.setQueryData<Schemas['IdeaAutoItemResponse'][]>(
        autosQuery().queryKey,
        (items) => items && ids.flatMap((id) => items.filter((item) => item.auto.id === id)),
      ),
    onSettled: () => client.invalidateQueries({ queryKey: ['idea-autos', 'list'] }),
  });
}

export function useDeleteAuto() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: deleteAuto,
    onSuccess: () => client.invalidateQueries({ queryKey: ['idea-autos'] }),
  });
}
