import type { ListSort } from '@/shared/ui/PagedTable';

import { ApiError, type RequestError } from './client';

/** The request of a page of a list the server sorts (change add-ideas-and-market, design.md, decision 10). */
export interface PageRequest {
  readonly sort: ListSort;
  readonly page: number;
  readonly pageSize: number;
  readonly filter?: string;
}

/** The query of a page: its order, number and size, and the saved filter. */
export function pageQuery(request: PageRequest) {
  return {
    sort: request.sort.field,
    direction: request.sort.descending ? 'desc' : 'asc',
    page: request.page,
    pageSize: request.pageSize,
    ...(request.filter !== undefined ? { filter: request.filter } : {}),
  };
}

/** A list refused with errors at their places (`422`): a field of the sort, a page or a filter of another place. */
export class ListRefused extends ApiError {
  constructor(readonly errors: readonly RequestError[]) {
    super(422, errors.map((error) => error.reason).join('; '));
    this.name = 'ListRefused';
  }
}

/** The data of a page, or {@link ListRefused}, or the error of {@link ApiError}. */
export function pageOf<T>(result: { data?: T; error?: unknown; response: Response }): T {
  if (result.response.ok) {
    return result.data as T;
  }

  if (
    result.response.status === 422 &&
    result.error &&
    typeof result.error === 'object' &&
    'errors' in result.error
  ) {
    throw new ListRefused((result.error as { errors: RequestError[] }).errors);
  }

  const problem = (result.error ?? {}) as { title?: string; detail?: string };
  throw new ApiError(
    result.response.status,
    problem.detail ?? problem.title ?? result.response.statusText,
  );
}
