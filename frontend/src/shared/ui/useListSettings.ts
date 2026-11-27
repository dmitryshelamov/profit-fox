import { useState } from 'react';

import { readStored, writeStored } from '@/preview/stored';

import type { ListSort } from './PagedTable';
import { pageSizes } from './Pager';

/**
 * The sort and the size of a page of one table, kept in the browser apart for each table (spec bench-ui «Сортировка по
 * колонке», «Страницы списка»). The page itself stands in the address and is the page's.
 */
export function useListSettings(table: string, start: ListSort) {
  const sortKey = `profit-fox.list.${table}.sort`;
  const sizeKey = `profit-fox.list.${table}.pageSize`;
  const [sort, setSort] = useState<ListSort>(() => {
    const stored = readStored<ListSort | null>(sortKey, null);
    return stored !== null &&
      typeof stored.field === 'string' &&
      typeof stored.descending === 'boolean'
      ? stored
      : start;
  });
  const [pageSize, setPageSize] = useState<number>(() => {
    const stored = readStored<number>(sizeKey, 50);
    return (pageSizes as readonly number[]).includes(stored) ? stored : 50;
  });

  return {
    sort,
    pageSize,
    setSort: (next: ListSort) => {
      setSort(next);
      writeStored(sortKey, next);
    },
    setPageSize: (next: number) => {
      setPageSize(next);
      writeStored(sizeKey, next);
    },
  };
}

/** The page of the address: a whole number from 1, the first one otherwise. */
export function pageSearch(search: Record<string, unknown>): { page?: number } {
  const page = Number(search.page);
  return Number.isInteger(page) && page > 1 ? { page } : {};
}
