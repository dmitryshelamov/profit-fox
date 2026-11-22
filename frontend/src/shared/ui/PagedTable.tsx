import type { ReactNode } from 'react';

import { Table, Td, Th } from './Table';

/** The order of a list sorted on the server: its field and its direction. */
export interface ListSort {
  readonly field: string;
  readonly descending: boolean;
}

/** A column of a list sorted on the server. */
export interface PagedColumn<Row> {
  readonly key: string;
  readonly title: string;
  /** A text column sorts from A on the first click; numbers, times and results from the largest. */
  readonly text?: boolean;
  /** A number goes on the right in mono. */
  readonly numeric?: boolean;
  /** A column without a sort has a plain head. */
  readonly unsorted?: boolean;
  readonly className?: string;
  readonly cell: (row: Row) => ReactNode;
}

/**
 * The order after a click on the head of a column (spec bench-ui «Сортировка по колонке»): another column sorts by it —
 * text from A, the rest from the largest; the same column turns the direction. There is no state without a sort.
 */
export function nextSort(current: ListSort, column: { key: string; text?: boolean }): ListSort {
  return current.field === column.key
    ? { field: column.key, descending: !current.descending }
    : { field: column.key, descending: column.text !== true };
}

/**
 * A table of one page of a list the server sorts (change add-ideas-and-market, design.md, decision 13): every head is a
 * button, the head of the sort is lit and carries the arrow of its direction. A row opens its page by `onOpen`.
 */
export function PagedTable<Row>({
  label,
  columns,
  rows,
  sort,
  rowKey,
  rowLabel,
  onSort,
  onOpen,
}: {
  readonly label: string;
  readonly columns: readonly PagedColumn<Row>[];
  readonly rows: readonly Row[];
  readonly sort: ListSort;
  readonly rowKey: (row: Row) => string;
  readonly rowLabel?: (row: Row) => string;
  readonly onSort: (sort: ListSort) => void;
  readonly onOpen?: (row: Row) => void;
}) {
  return (
    <Table label={label}>
      <thead>
        <tr>
          {columns.map((column) => {
            const active = column.key === sort.field;
            return (
              <Th
                key={column.key}
                aria-sort={active ? (sort.descending ? 'descending' : 'ascending') : undefined}
                className={`${column.numeric ? 'pr-0 text-right' : ''} ${column.className ?? ''}`}
              >
                {column.unsorted ? (
                  column.title
                ) : (
                  <button
                    type="button"
                    className={`cursor-pointer uppercase hover:text-text ${active ? 'text-accent' : ''}`}
                    onClick={() => onSort(nextSort(sort, column))}
                  >
                    {column.title}
                    <span aria-hidden="true" className={`ml-1 ${active ? '' : 'text-faint'}`}>
                      {active ? (sort.descending ? '↓' : '↑') : '↕'}
                    </span>
                  </button>
                )}
              </Th>
            );
          })}
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => (
          <tr
            key={rowKey(row)}
            aria-label={rowLabel?.(row)}
            className={`h-11 border-t border-line hover:bg-panel-2 ${onOpen ? 'cursor-pointer' : ''}`}
            onClick={onOpen ? () => onOpen(row) : undefined}
          >
            {columns.map((column) => (
              <Td
                key={column.key}
                className={`${column.numeric ? 'pr-0 text-right font-mono' : ''} ${column.className ?? ''}`}
              >
                {column.cell(row)}
              </Td>
            ))}
          </tr>
        ))}
      </tbody>
    </Table>
  );
}
