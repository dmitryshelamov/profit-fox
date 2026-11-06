import { Link, useNavigate } from '@tanstack/react-router';

import { intervalLabel } from '@/modules/market-data';

import type { MarketRow } from '../model/rows';
import { setupLabel, setupsOn } from '../model/setups';
import type { Sort, SortColumn } from '../model/sorting';
import { CategoryBadge } from './CategoryBadge';
import { type MetricColumn, metricColumns } from './metricColumns';

const cell = 'px-2 py-3';
const number = `${cell} text-right font-mono whitespace-nowrap`;

const ariaSort = (sort: Sort | null, column: SortColumn) => {
  if (sort?.column !== column) {
    return undefined;
  }

  return sort.direction === 'asc' ? 'ascending' : 'descending';
};

/** The groups of the header row above, each over its columns; a column of no group spans both rows. */
const groups = metricColumns.reduce<{ title: string | undefined; columns: MetricColumn[] }[]>(
  (all, column) => {
    const last = all.at(-1);

    if (column.group !== undefined && last?.title === column.group) {
      last.columns.push(column);
    } else {
      all.push({ title: column.group, columns: [column] });
    }

    return all;
  },
  [],
);

function SortButton({
  title,
  label,
  column,
  sort,
  onSort,
}: {
  title: string;
  label: string;
  column: SortColumn;
  sort: Sort | null;
  onSort: (column: SortColumn) => void;
}) {
  const sorted = sort?.column === column;

  return (
    <button
      type="button"
      aria-label={label}
      onClick={() => onSort(column)}
      className={`uppercase hover:text-text ${sorted ? 'text-accent' : ''}`}
    >
      {title}
      {sorted && <span aria-hidden="true"> {sort.direction === 'asc' ? '↑' : '↓'}</span>}
    </button>
  );
}

/**
 * The pairs of the market with the headers that sort. Clicking a row opens the pair; its symbol is also a link, for
 * the keyboard. A table of stale data is dimmed.
 */
export function MarketTable({
  rows,
  sort,
  onSort,
  dimmed,
  setupInterval,
}: {
  rows: readonly MarketRow[];
  sort: Sort | null;
  onSort: (column: SortColumn) => void;
  dimmed: boolean;
  /** The interval of the setups the column shows. */
  setupInterval: string;
}) {
  const navigate = useNavigate();

  return (
    <div className="overflow-x-auto">
      <table
        className={`w-full border-separate border-spacing-y-1 text-sm ${dimmed ? 'opacity-50' : ''}`}
      >
        <thead className="bg-panel text-left text-[11px] tracking-wide text-faint uppercase">
          <tr>
            <th
              scope="col"
              rowSpan={2}
              aria-sort={ariaSort(sort, 'symbol')}
              className={`${cell} align-bottom`}
            >
              <SortButton
                title="Инструмент"
                label="Инструмент"
                column="symbol"
                sort={sort}
                onSort={onSort}
              />
            </th>
            <th scope="col" rowSpan={2} className={`${cell} align-bottom`}>
              Сетап · {intervalLabel(setupInterval)}
            </th>
            {groups.map((group) =>
              group.title === undefined ? (
                group.columns.map((column) => (
                  <th
                    key={column.column}
                    scope="col"
                    rowSpan={2}
                    aria-sort={ariaSort(sort, column.column)}
                    className={`${cell} text-right align-bottom`}
                  >
                    <SortButton
                      title={column.title}
                      label={column.label}
                      column={column.column}
                      sort={sort}
                      onSort={onSort}
                    />
                  </th>
                ))
              ) : (
                <th
                  key={group.title}
                  scope="colgroup"
                  colSpan={group.columns.length}
                  className="px-2 pt-3 text-right font-normal"
                >
                  <span className="block border-b border-line pb-1">{group.title}</span>
                </th>
              ),
            )}
          </tr>
          <tr>
            {groups
              .filter((group) => group.title !== undefined)
              .flatMap((group) => group.columns)
              .map((column) => (
                <th
                  key={column.column}
                  scope="col"
                  aria-sort={ariaSort(sort, column.column)}
                  className="px-2 pb-3 text-right"
                >
                  <SortButton
                    title={column.title}
                    label={column.label}
                    column={column.column}
                    sort={sort}
                    onSort={onSort}
                  />
                </th>
              ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr
              key={`${row.category} ${row.symbol}`}
              onClick={() =>
                void navigate({
                  to: '/market/$category/$symbol',
                  params: { category: row.category, symbol: row.symbol },
                  search: {},
                })
              }
              className="cursor-pointer bg-panel hover:bg-panel-2"
            >
              <td className={`${cell} whitespace-nowrap`}>
                <CategoryBadge category={row.category} />{' '}
                <Link
                  to="/market/$category/$symbol"
                  params={{ category: row.category, symbol: row.symbol }}
                  search={{}}
                  className="ml-1.5 font-semibold hover:underline"
                >
                  {row.symbol}
                </Link>
                {row.incomplete && (
                  <span
                    role="img"
                    aria-label="Данные догружаются"
                    title="Данные догружаются"
                    className="ml-2 inline-flex size-4 items-center justify-center rounded-full bg-warn/25 text-[10px] font-bold text-warn"
                  >
                    !
                  </span>
                )}
              </td>
              <td className={cell}>
                <span className="flex flex-wrap gap-1">
                  {setupsOn(row, setupInterval).map((setup) => (
                    <span
                      key={setup}
                      className="rounded bg-panel-2 px-1.5 py-0.5 text-xs whitespace-nowrap"
                    >
                      {setupLabel(setup)}
                    </span>
                  ))}
                </span>
              </td>
              {metricColumns.map(({ column, render }) => (
                <td key={column} className={number}>
                  {render(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
