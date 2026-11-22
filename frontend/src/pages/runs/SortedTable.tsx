import { Link } from '@tanstack/react-router';
import { type ReactNode, useState } from 'react';

import { Table, Td, Th } from '@/shared/ui/Table';

/** A table longer than this many rows scrolls inside its own area under a head that stays. */
export const longTableRows = 12;

/** A column of a sorted table: what it shows and the value it sorts by; a value of none sorts after the others. */
export interface SortedColumn<Row> {
  readonly key: string;
  readonly title: string;
  readonly className?: string;
  /** A number goes on the right in mono. */
  readonly numeric?: boolean;
  readonly value: (row: Row) => number | string | null | undefined;
  readonly cell: (row: Row) => ReactNode;
}

/** The order of a table: its column and its direction. */
export interface SortOrder {
  readonly key: string;
  readonly descending: boolean;
}

function compare(a: number | string | null | undefined, b: number | string | null | undefined) {
  const none = (value: unknown) => value === null || value === undefined || value === '';
  if (none(a) || none(b)) {
    return none(a) === none(b) ? 0 : none(a) ? 1 : -1;
  }

  const left = Number(a);
  const right = Number(b);
  return Number.isNaN(left) || Number.isNaN(right)
    ? String(a).localeCompare(String(b))
    : left - right;
}

/**
 * A table sorted on the client by any of its columns (change add-strategy-backtest, design.md, decision 10): a click on
 * the head of a column sorts by it from the largest, a second click from the smallest; a value of none stands last in
 * both. A row of a set is a link to its page stretched over the whole row, as the row of a finished run. A table longer than
 * {@link longTableRows} rows scrolls inside its area under a head that stays (spec bench-run-queue «Двести наборов»).
 */
export function SortedTable<Row>({
  label,
  columns,
  rows,
  start,
  rowLabel,
  link,
}: {
  readonly label: string;
  readonly columns: readonly SortedColumn<Row>[];
  readonly rows: readonly Row[];
  readonly start: SortOrder;
  readonly rowLabel: (row: Row) => string;
  /** The set of a run a row opens; a table without it has rows that open nothing. */
  readonly link?: (row: Row) => { readonly id: string; readonly n: string };
}) {
  const [order, setOrder] = useState(start);
  const column = columns.find((candidate) => candidate.key === order.key);
  const sorted = column
    ? [...rows].sort((a, b) => {
        const by = compare(column.value(a), column.value(b));
        const none = (row: Row) => {
          const value = column.value(row);
          return value === null || value === undefined || value === '';
        };
        // A value of none stays last whichever the direction.
        return none(a) || none(b) ? by : order.descending ? -by : by;
      })
    : rows;
  const long = rows.length > longTableRows;

  return (
    <Table label={label} className={long ? 'max-h-[520px] overflow-y-auto' : ''}>
      <thead className={long ? 'sticky top-0 z-10 bg-panel' : ''} data-sticky={long || undefined}>
        <tr>
          {columns.map((item) => {
            const sortedBy = item.key === order.key;
            return (
              <Th
                key={item.key}
                aria-sort={sortedBy ? (order.descending ? 'descending' : 'ascending') : 'none'}
                className={`${item.numeric ? 'text-right' : ''} ${item.className ?? ''}`}
              >
                <button
                  type="button"
                  className={`cursor-pointer hover:text-text ${sortedBy ? 'text-text' : ''}`}
                  onClick={() =>
                    setOrder({
                      key: item.key,
                      descending: sortedBy ? !order.descending : true,
                    })
                  }
                >
                  {item.title}
                  <span aria-hidden="true" className="ml-1">
                    {sortedBy ? (order.descending ? '↓' : '↑') : ''}
                  </span>
                </button>
              </Th>
            );
          })}
          {link && (
            <Th className="w-6">
              <span className="sr-only">Открыть</span>
            </Th>
          )}
        </tr>
      </thead>
      <tbody>
        {sorted.map((row) => {
          const target = link?.(row);
          return (
            <tr
              key={rowLabel(row)}
              aria-label={rowLabel(row)}
              className={`h-10 border-t border-line hover:bg-panel-2 ${target ? 'relative' : ''}`}
            >
              {columns.map((item, index) => (
                <Td
                  key={item.key}
                  className={`${item.numeric ? 'text-right font-mono' : ''} ${item.className ?? ''}`}
                >
                  {index === 0 && target ? (
                    <Link
                      to="/runs/$id/sets/$n"
                      params={target}
                      className="outline-none after:absolute after:inset-0 after:content-[''] focus-visible:after:rounded-md focus-visible:after:outline focus-visible:after:outline-accent"
                    >
                      {item.cell(row)}
                    </Link>
                  ) : (
                    item.cell(row)
                  )}
                </Td>
              ))}
              {target && (
                <Td className="text-faint">
                  <span aria-hidden="true">›</span>
                </Td>
              )}
            </tr>
          );
        })}
      </tbody>
    </Table>
  );
}
