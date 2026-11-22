import { Badge } from '@/shared/ui/Badge';

import { ListHead, ListRow, MomentButton, tightCell } from './lists';
import { formatUtc } from './preview';
import { exitOf, percent, type ShownCycle, usdt } from './strategyPreview';

/**
 * The cycles of a strategy (spec strategy-constructor «Вкладки предпросмотра стратегии»): each with the moment of its
 * entry in UTC, its side, the number of its entries, the largest money put in, how it ended — or that it is open — and
 * its result after costs as a percent and in USDT in the color of its sign. A click on a moment shows the cycle on the
 * chart.
 *
 * The six columns fit the 470 px of the right part of the panel, less a scrollbar: the cells are tight, the moment is
 * of 12 px on one line, the head of the entries is short, and only the exit gives way to the others — it breaks before
 * the time the cycle was held.
 */
export function TradeList({
  cycles,
  selected,
  markClicks,
  onPick,
}: {
  readonly cycles: readonly ShownCycle[];
  /** The moment of the order that opened the selected cycle. */
  readonly selected: number | null;
  /** How many times a mark of the chart was clicked: each click brings the selected row into view. */
  readonly markClicks: number;
  readonly onPick: (cycle: ShownCycle) => void;
}) {
  if (cycles.length === 0) {
    return <p className="p-4 text-muted">Сделок нет.</p>;
  }

  return (
    <table aria-label="Сделки" className="w-full border-collapse text-[13px]">
      <ListHead
        tight
        columns={[
          ['Вход, UTC'],
          ['Сторона'],
          ['Вх.', 'text-right', 'Входов'],
          ['Вложено', 'text-right'],
          ['Выход', 'pl-2'],
          ['Результат', 'text-right'],
        ]}
      />
      <tbody>
        {cycles.map((shown) => {
          const { cycle, key } = shown;
          const net = cycle.net == null ? null : Number(cycle.net);
          const tone = net !== null && net < 0 ? 'err' : 'ok';
          const [ended, held] = exitOf(cycle).split(' · ');
          return (
            <ListRow
              key={key}
              label={`Сделка ${formatUtc(key)}`}
              selected={key === selected}
              markClicks={markClicks}
            >
              <td className={`${tightCell} text-xs whitespace-nowrap`}>
                <MomentButton moment={formatUtc(key)} onClick={() => onPick(shown)} />
              </td>
              <td className={tightCell}>
                <Badge tone={cycle.side === 'short' ? 'err' : 'ok'}>
                  {cycle.side === 'short' ? 'шорт' : 'лонг'}
                </Badge>
              </td>
              <td className={`${tightCell} text-right font-mono`}>{Number(cycle.entries)}</td>
              <td className={`${tightCell} text-right font-mono`}>
                {Number(cycle.invested).toFixed(2)}
              </td>
              <td className={`${tightCell} pl-2 text-xs text-muted`}>
                {held === undefined ? ended : `${ended} · `}
                {held !== undefined && <span className="whitespace-nowrap">{held}</span>}
              </td>
              <td className={`${tightCell} text-right`}>
                {net === null ? (
                  '—'
                ) : (
                  <span
                    data-tone={tone}
                    className={`flex flex-col items-end font-mono whitespace-nowrap ${tone === 'err' ? 'text-err' : 'text-ok'}`}
                  >
                    <span>{percent(net)}</span>
                    <span className="text-[11px]">{usdt(Number(cycle.netAmount ?? 0))}</span>
                  </span>
                )}
              </td>
            </ListRow>
          );
        })}
      </tbody>
    </table>
  );
}
