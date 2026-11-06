import { formatMoment } from '@/shared/format/format';

import { durationText, ideaOutcomeText, promiseHint, promiseText } from '../model/journal';
import { resultText } from '../model/result';
import {
  byParts,
  cell,
  head,
  Heads,
  type IdeaTrade,
  InstrumentCells,
  LevelCells,
  StatusCell,
} from './tradeCells';

/** The closed trades: the result, the time to the outcome, what the idea promised at the entry and how the idea ended. */
export function ClosedTradesTable({ trades }: { trades: readonly IdeaTrade[] }) {
  return (
    <div className="overflow-x-auto">
      <table
        aria-label="Закрытые сделки"
        className="w-full border-separate border-spacing-y-1 text-sm"
      >
        <thead className={head}>
          <tr>
            <Heads
              names={[
                'Закрыта',
                'Инструмент',
                'Сетап',
                'Сторона',
                'Вход',
                'Стоп',
                'Цель',
                'Объём',
                'Статус',
                'Результат',
                'До исхода',
                'Обещание',
                'Идея',
              ]}
              right={['Вход', 'Стоп', 'Цель', 'Объём']}
            />
          </tr>
        </thead>
        <tbody>
          {trades.map((trade) => (
            <tr key={trade.id} className="bg-panel">
              <td className={`${cell} font-mono whitespace-nowrap text-muted`}>
                {formatMoment(trade.statusAt)}
              </td>
              <InstrumentCells trade={trade} />
              <LevelCells trade={trade} risk={false} />
              <StatusCell trade={trade} />
              <td className={`${cell} font-mono`}>
                {byParts(resultText(trade.result, trade.resultR))}
              </td>
              <td className={`${cell} whitespace-nowrap`}>
                {durationText(trade.secondsToOutcome)}
              </td>
              <td
                className={`${cell} font-mono`}
                title={trade.promise === null ? undefined : promiseHint(trade.promise)}
              >
                {byParts(promiseText(trade.promise))}
              </td>
              <td className={`${cell} font-mono whitespace-nowrap`}>
                {ideaOutcomeText(trade.ideaOutcome)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
