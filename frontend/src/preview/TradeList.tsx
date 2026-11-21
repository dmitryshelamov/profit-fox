import type { PreviewOrder } from '@/api/client';
import { Badge } from '@/shared/ui/Badge';

import { ListHead, ListRow, MomentButton } from './lists';
import { formatUtc } from './preview';
import { exitOf, percent, tradesOf } from './strategyPreview';

/**
 * The trades of a strategy (spec strategy-constructor «Вкладки предпросмотра стратегии»): the orders that opened a
 * trade, each with the moment of its entry in UTC, its side, how it ended and its result after costs in the color of
 * its sign. A click on a moment shows the trade on the chart.
 */
export function TradeList({
  orders,
  selected,
  onPick,
}: {
  readonly orders: readonly PreviewOrder[];
  /** The moment of the order of the selected trade. */
  readonly selected: number | null;
  readonly onPick: (order: PreviewOrder) => void;
}) {
  const trades = tradesOf(orders);
  if (trades.length === 0) {
    return <p className="p-4 text-muted">Сделок нет.</p>;
  }

  return (
    <table aria-label="Сделки" className="w-full border-collapse text-[13px]">
      <ListHead columns={[['Вход, UTC'], ['Сторона'], ['Выход'], ['Результат', 'text-right']]} />
      <tbody>
        {trades.map((order) => {
          const net = Number(order.trade!.net);
          return (
            <ListRow
              key={order.moment}
              label={`Сделка ${formatUtc(order.moment)}`}
              selected={Number(order.moment) === selected}
            >
              <td className="px-4">
                <MomentButton moment={formatUtc(order.moment)} onClick={() => onPick(order)} />
              </td>
              <td className="px-4">
                <Badge tone={order.side === 'short' ? 'err' : 'ok'}>
                  {order.side === 'short' ? 'шорт' : 'лонг'}
                </Badge>
              </td>
              <td className="px-4 text-xs text-muted">{exitOf(order)}</td>
              <td className="px-4 text-right">
                <span
                  data-tone={net < 0 ? 'err' : 'ok'}
                  className={`font-mono ${net < 0 ? 'text-err' : 'text-ok'}`}
                >
                  {percent(net)}
                </span>
              </td>
            </ListRow>
          );
        })}
      </tbody>
    </table>
  );
}
