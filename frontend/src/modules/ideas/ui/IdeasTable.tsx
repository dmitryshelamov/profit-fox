import { Link, useNavigate } from '@tanstack/react-router';

import { CategoryBadge, setupLabel } from '@/modules/market';
import { intervalLabel } from '@/modules/market-data';
import { formatMoment, groupDigits, showDecimal } from '@/shared/format/format';

import { typeLabel } from '../model/labels';
import { expectancyText, riskOutcome } from '../model/stats';
import { type Idea, ProbabilityCell, SideText, StatusText } from './IdeaCells';

const cell = 'px-3 py-3';
const number = `${cell} text-right font-mono whitespace-nowrap`;

const headers = [
  'Время',
  'Инструмент',
  'Сетап',
  'ТФ',
  'Тип',
  'Сторона',
  'Вход',
  'Стоп',
  'Цель',
  'R:R',
  'Вероятность',
  'Ожидание',
  'Прибыль',
  'Убыток',
  'Статус',
];

const rightAligned = new Set(['Вход', 'Стоп', 'Цель', 'R:R', 'Ожидание', 'Прибыль', 'Убыток']);

/**
 * The ideas as a table. Clicking a row opens the pair with the idea selected; the symbol is also a link, for the keyboard.
 * The profit and the loss come from the risk per idea.
 */
export function IdeasTable({ ideas, risk }: { ideas: readonly Idea[]; risk: string }) {
  const navigate = useNavigate();

  return (
    <table className="w-full border-separate border-spacing-y-1 text-sm">
      <thead className="bg-panel text-left text-[11px] tracking-wide text-faint uppercase">
        <tr>
          {headers.map((header) => (
            <th
              key={header}
              scope="col"
              className={`${cell} ${rightAligned.has(header) ? 'text-right' : ''}`}
            >
              {header}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {ideas.map((idea) => {
          const outcome = riskOutcome(idea.riskReward, risk);

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
              <td className={number}>{groupDigits(idea.entry)}</td>
              <td className={number}>{groupDigits(idea.stop)}</td>
              <td className={number}>{groupDigits(idea.target)}</td>
              <td className={number}>{showDecimal(idea.riskReward, 2)}</td>
              <td className={cell}>
                <ProbabilityCell idea={idea} />
              </td>
              <td className={number}>{expectancyText(idea.bucket)}</td>
              <td className={`${number} text-ok`}>{outcome?.profit ?? '—'}</td>
              <td className={`${number} text-err`}>{outcome?.loss ?? '—'}</td>
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
