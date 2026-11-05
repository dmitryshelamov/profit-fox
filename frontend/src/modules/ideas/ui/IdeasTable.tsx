import { Link, useNavigate } from '@tanstack/react-router';

import { CategoryBadge } from '@/modules/market';
import { formatMoment } from '@/shared/format/format';

import type { IdeaSort, IdeaSortColumn } from '../model/sort';
import type { Idea } from './IdeaCells';
import {
  cell,
  firedCell,
  firedColumn,
  type IdeaColumn,
  ideaColumns,
  IdeaRowCells,
} from './IdeaRow';

const headers: readonly IdeaColumn[] = [
  firedColumn,
  { column: 'symbol', label: 'Инструмент' },
  ...ideaColumns,
];

/**
 * The ideas as a table. Clicking a row opens the pair with the idea selected; the symbol is also a link, for the keyboard.
 * The profit and the loss, in one column, come from the amount per idea. A header sorts the table: it shows the column and the
 * direction of the sorting, and asks `onSort` for a column when it is clicked.
 */
export function IdeasTable({
  ideas,
  amount,
  sort,
  onSort,
}: {
  ideas: readonly Idea[];
  amount: string;
  sort: IdeaSort;
  onSort: (column: IdeaSortColumn) => void;
}) {
  const navigate = useNavigate();

  return (
    <table className="w-full border-separate border-spacing-y-1 text-sm">
      <thead className="bg-panel text-left text-[11px] tracking-wide text-faint uppercase">
        <tr>
          {headers.map(({ column, label, right }) => (
            <th
              key={column}
              scope="col"
              aria-sort={
                sort.column !== column
                  ? 'none'
                  : sort.direction === 'asc'
                    ? 'ascending'
                    : 'descending'
              }
              className={`${cell} ${right ? 'text-right' : ''}`}
            >
              <button
                type="button"
                onClick={() => onSort(column)}
                className="tracking-wide uppercase hover:text-text"
              >
                {label}
                {sort.column === column && (sort.direction === 'asc' ? ' ↑' : ' ↓')}
              </button>
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {ideas.map((idea) => (
          <tr
            key={idea.id}
            onClick={() =>
              void navigate({
                to: '/market/$category/$symbol',
                params: { category: idea.category, symbol: idea.symbol },
                search: { idea: idea.id },
              })
            }
            className="cursor-pointer bg-panel hover:bg-panel-2"
          >
            <td className={firedCell}>{formatMoment(idea.firedAt)}</td>
            <td className={`${cell} whitespace-nowrap`}>
              <CategoryBadge category={idea.category} />{' '}
              <Link
                to="/market/$category/$symbol"
                params={{ category: idea.category, symbol: idea.symbol }}
                search={{ idea: idea.id }}
                className="ml-1.5 font-semibold hover:underline"
              >
                {idea.symbol}
              </Link>
            </td>
            <IdeaRowCells idea={idea} amount={amount} />
          </tr>
        ))}
      </tbody>
    </table>
  );
}
