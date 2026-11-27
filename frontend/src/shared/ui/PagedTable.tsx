import { type ReactNode, useEffect, useRef } from 'react';

import { Table, Td, Th } from './Table';

/** The order of a list sorted on the server: its field and its direction. */
export interface ListSort {
  readonly field: string;
  readonly descending: boolean;
}

/** A column of a list sorted on the server. A number, a time or a result stays on one line (task 20.8). */
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
  /** The classes of the cells of the body only, not of the head. */
  readonly cellClassName?: string;
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
  selected = null,
  dense = false,
}: {
  readonly label: string;
  readonly columns: readonly PagedColumn<Row>[];
  readonly rows: readonly Row[];
  readonly sort: ListSort;
  readonly rowKey: (row: Row) => string;
  readonly rowLabel?: (row: Row) => string;
  readonly onSort: (sort: ListSort) => void;
  readonly onOpen?: (row: Row) => void;
  /** The key of the chosen row: lit, and scrolled into view when it changes. */
  readonly selected?: string | null;
  /**
   * Cells 14 px apart instead of 20, for the tables of ideas: with the column «Балл» they would not fit 1100 px otherwise
   * (change add-idea-score, task 3.6).
   */
  readonly dense?: boolean;
}) {
  const body = useRef<HTMLTableSectionElement>(null);
  // Scrolled once for a choice, when its row is there: new answers of the server leave the page where it is (task 18.23).
  const scrolled = useRef<string | null>(null);
  useEffect(() => {
    if (!selected) {
      scrolled.current = null;
      return;
    }

    if (scrolled.current === selected) {
      return;
    }

    const row = body.current?.querySelector<HTMLElement>(`[data-key="${CSS.escape(selected)}"]`);
    if (row) {
      row.scrollIntoView?.({ block: 'nearest' });
      scrolled.current = selected;
    }
  }, [selected, rows]);

  return (
    <Table label={label} className={dense ? '[&_td]:pl-3.5 [&_th]:pl-3.5' : ''}>
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
      <tbody ref={body}>
        {rows.map((row) => (
          <tr
            key={rowKey(row)}
            data-key={rowKey(row)}
            aria-label={rowLabel?.(row)}
            aria-selected={selected === null ? undefined : selected === rowKey(row)}
            className={`h-11 border-t border-line hover:bg-panel-2 ${onOpen ? 'cursor-pointer' : ''} ${selected === rowKey(row) ? 'bg-accent/10' : ''}`}
            onClick={onOpen ? () => onOpen(row) : undefined}
          >
            {columns.map((column) => (
              <Td
                key={column.key}
                className={`${column.numeric ? 'pr-0 text-right font-mono whitespace-nowrap' : ''} ${column.className ?? ''} ${column.cellClassName ?? ''}`}
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
