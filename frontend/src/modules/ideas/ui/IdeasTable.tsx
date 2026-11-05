import { Link, useNavigate } from '@tanstack/react-router';

import { CategoryBadge, setupLabel } from '@/modules/market';
import { intervalLabel } from '@/modules/market-data';
import { formatMoment, showDecimal } from '@/shared/format/format';

import { typeLabel } from '../model/labels';
import { priceByTick } from '../model/price';
import type { IdeaSort, IdeaSortColumn } from '../model/sort';
import { moneyOutcome } from '../model/stats';
import { ExpectancyCell, type Idea, ProbabilityCell, SideText, StatusText } from './IdeaCells';

const cell = 'px-3 py-3';
const number = `${cell} text-right font-mono whitespace-nowrap`;

const headers: readonly { column: IdeaSortColumn; label: string }[] = [
  { column: 'firedAt', label: 'Найдена' },
  { column: 'symbol', label: 'Инструмент' },
  { column: 'setup', label: 'Сетап' },
  { column: 'interval', label: 'ТФ' },
  { column: 'type', label: 'Тип идеи' },
  { column: 'side', label: 'Сторона' },
  { column: 'entry', label: 'Вход' },
  { column: 'stop', label: 'Стоп' },
  { column: 'target', label: 'Цель' },
  { column: 'riskReward', label: 'R:R' },
  { column: 'probability', label: 'Вероятность' },
  { column: 'expectancy', label: 'Ожидание' },
  { column: 'profit', label: 'Прибыль / убыток' },
  { column: 'status', label: 'Статус' },
];

const rightAligned = new Set<IdeaSortColumn>([
  'entry',
  'stop',
  'target',
  'riskReward',
  'expectancy',
  'profit',
]);

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
          {headers.map(({ column, label }) => (
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
              className={`${cell} ${rightAligned.has(column) ? 'text-right' : ''}`}
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
        {ideas.map((idea) => {
          const outcome = moneyOutcome(idea, amount);

          return (
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
              <td className={`${cell} font-mono whitespace-nowrap text-muted`}>
                {formatMoment(idea.firedAt)}
              </td>
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
              <td className={cell}>{setupLabel(idea.setup)}</td>
              <td className={`${cell} whitespace-nowrap`}>{intervalLabel(idea.interval)}</td>
              <td className={`${cell} whitespace-nowrap`}>{typeLabel(idea.type)}</td>
              <td className={cell}>
                <SideText side={idea.side} />
              </td>
              <td className={number}>{priceByTick(idea.entry, idea.tick)}</td>
              <td className={number}>{priceByTick(idea.stop, idea.tick)}</td>
              <td className={number}>{priceByTick(idea.target, idea.tick)}</td>
              <td className={number}>{showDecimal(idea.riskReward, 2)}</td>
              <td className={cell}>
                <ProbabilityCell idea={idea} />
              </td>
              <td className={number}>
                <ExpectancyCell idea={idea} />
              </td>
              <td className={number}>
                {outcome === null ? (
                  '—'
                ) : (
                  <>
                    <span className="text-ok">{outcome.profit}</span> /{' '}
                    <span className="text-err">{outcome.loss}</span>
                  </>
                )}
              </td>
              <td className={`${cell} whitespace-nowrap`}>
                <StatusText status={idea.status} />
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
