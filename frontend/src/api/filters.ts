import { api, type Schemas, unwrap } from './client';
import { type Written, written } from './signals';

export type SavedFilter = Schemas['FilterResponse'];
export type FilterCondition = Schemas['FilterConditionDto'];
export type FilterField = Schemas['FilterFieldResponse'];
export type FilterCount = Schemas['FilterCountResponse'];

/** The place of a filter: «Рынок», «Идеи» or both. */
export type FilterPlace = 'market' | 'ideas' | 'both';

export async function listFilters(): Promise<SavedFilter[]> {
  return unwrap(await api.GET('/api/filters'));
}

export async function listFilterFields(): Promise<FilterField[]> {
  return unwrap(await api.GET('/api/filters/fields'));
}

export async function saveFilter(
  id: string | null,
  name: string,
  place: FilterPlace,
  conditions: readonly FilterCondition[],
): Promise<Written<SavedFilter>> {
  const body = { name, place, conditions: [...conditions] };
  return written(
    id === null
      ? await api.POST('/api/filters', { body })
      : await api.PUT('/api/filters/{id}', { params: { path: { id } }, body }),
  );
}

export async function deleteFilter(id: string): Promise<void> {
  const result = await api.DELETE('/api/filters/{id}', { params: { path: { id } } });
  if (!result.response.ok && result.response.status !== 404) {
    unwrap(result);
  }
}

/** How many pairs and open ideas a draft of conditions matches, or the errors of its conditions. */
export async function countFilter(
  conditions: readonly FilterCondition[],
): Promise<Written<FilterCount>> {
  return written(await api.POST('/api/filters/count', { body: { conditions: [...conditions] } }));
}
