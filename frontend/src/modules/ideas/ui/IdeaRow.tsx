import { setupLabel } from '@/modules/market';
import { intervalLabel } from '@/modules/market-data';

import { typeLabel } from '../model/labels';
import { priceByTick } from '../model/price';
import type { IdeaSortColumn } from '../model/sort';
import { moneyOutcome } from '../model/stats';
import {
  ExpectancyCell,
  type Idea,
  ProbabilityCell,
  RiskRewardCell,
  SideText,
  StatusText,
} from './IdeaCells';

export const cell = 'px-3 py-3';
const number = `${cell} text-right font-mono whitespace-nowrap`;

export interface IdeaColumn {
  column: IdeaSortColumn;
  label: string;
  right?: boolean;
}

/** The first column of a table of ideas: when the idea was found. */
export const firedColumn: IdeaColumn = { column: 'firedAt', label: 'Найдена' };

/** The columns of an idea after the found one and the instrument, the same in every table of ideas. */
export const ideaColumns: readonly IdeaColumn[] = [
  { column: 'setup', label: 'Сетап' },
  { column: 'interval', label: 'ТФ' },
  { column: 'type', label: 'Тип идеи' },
  { column: 'side', label: 'Сторона' },
  { column: 'entry', label: 'Вход', right: true },
  { column: 'stop', label: 'Стоп', right: true },
  { column: 'target', label: 'Цель', right: true },
  { column: 'riskReward', label: 'R:R', right: true },
  { column: 'probability', label: 'Вероятность' },
  { column: 'expectancy', label: 'Ожидание', right: true },
  { column: 'profit', label: 'Прибыль / убыток', right: true },
  { column: 'status', label: 'Статус' },
];

/** The class of the cell of the found time. */
export const firedCell = `${cell} font-mono whitespace-nowrap text-muted`;

/** The cells of `ideaColumns` for an idea; the profit and the loss come from the amount per idea. */
export function IdeaRowCells({ idea, amount }: { idea: Idea; amount: string }) {
  const outcome = moneyOutcome(idea, amount);

  return (
    <>
      <td className={cell}>{setupLabel(idea.setup)}</td>
      <td className={`${cell} whitespace-nowrap`}>{intervalLabel(idea.interval)}</td>
      <td className={`${cell} whitespace-nowrap`}>{typeLabel(idea.type)}</td>
      <td className={cell}>
        <SideText side={idea.side} />
      </td>
      <td className={number}>{priceByTick(idea.entry, idea.tick)}</td>
      <td className={number}>{priceByTick(idea.stop, idea.tick)}</td>
      <td className={number}>{priceByTick(idea.target, idea.tick)}</td>
      <td className={number}>
        <RiskRewardCell netRiskReward={idea.netRiskReward} riskReward={idea.riskReward} />
      </td>
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
    </>
  );
}
