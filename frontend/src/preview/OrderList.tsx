import type { PreviewCycle, PreviewOrder } from '@/api/client';
import { zoneLabel } from '@/shared/time';

import { ListHead, ListRow, MomentButton } from './lists';
import { formatMoment } from './preview';
import { openersOf, outcomeOf, priceText } from './strategyPreview';

const sides: Record<string, string> = {
  long: 'лонг',
  short: 'шорт',
  buy: 'покупка',
  sell: 'продажа',
};

/**
 * The orders of a strategy in time order (spec strategy-constructor «Панель предпросмотра стратегии»): the moment in
 * the zone of the browser, the node, the side, the price of a fill and the outcome — how its cycle ended and its result, a fill, skipped,
 * without a trade and why, or not computed. A click on a moment shows the order on the chart.
 */
export function OrderList({
  orders,
  cycles,
  selected,
  markClicks,
  onPick,
}: {
  readonly orders: readonly PreviewOrder[];
  readonly cycles: readonly PreviewCycle[];
  /** The moment of the selected order. */
  readonly selected: number | null;
  /** How many times a mark of the chart was clicked: each click brings the selected row into view. */
  readonly markClicks: number;
  readonly onPick: (order: PreviewOrder) => void;
}) {
  if (orders.length === 0) {
    return <p className="p-4 text-muted">Заявок нет.</p>;
  }

  const openers = openersOf(orders);
  return (
    <table aria-label="Заявки" className="w-full border-collapse text-[13px]">
      <ListHead
        columns={[
          [`Момент, ${zoneLabel()}`],
          ['Узел'],
          ['Сторона'],
          ['Цена', 'text-right'],
          ['Исход'],
        ]}
      />
      <tbody>
        {orders.map((order, index) => (
          <ListRow
            key={`${order.moment}-${order.node ?? ''}-${index}`}
            label={`Заявка ${formatMoment(order.moment)}`}
            selected={Number(order.moment) === selected}
            markClicks={markClicks}
            dim={order.outcome !== 'filled'}
          >
            <td className="px-4">
              <MomentButton moment={formatMoment(order.moment)} onClick={() => onPick(order)} />
            </td>
            <td className="px-4 font-mono text-xs">{order.node ?? '—'}</td>
            <td className="px-4">{sides[order.side] ?? order.side}</td>
            <td className="px-4 text-right font-mono text-xs">
              {order.price == null ? '—' : priceText(Number(order.price))}
            </td>
            <td className="px-4 text-xs">{outcomeOf(order, cycles, openers.has(order))}</td>
          </ListRow>
        ))}
      </tbody>
    </table>
  );
}
