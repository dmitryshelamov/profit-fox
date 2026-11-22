import type { PreviewOrder } from '@/api/client';

import { ListHead, ListRow, MomentButton } from './lists';
import { formatUtc } from './preview';
import { outcomeOf } from './strategyPreview';

/**
 * The orders of a strategy in time order (spec strategy-constructor «Панель предпросмотра стратегии»): the moment in
 * UTC, the side and the outcome — the exit and the result after costs, skipped, without a trade and why, or not
 * computed. A click on a moment shows the order on the chart.
 */
export function OrderList({
  orders,
  selected,
  markClicks,
  onPick,
}: {
  readonly orders: readonly PreviewOrder[];
  /** The moment of the selected order. */
  readonly selected: number | null;
  /** How many times a mark of the chart was clicked: each click brings the selected row into view. */
  readonly markClicks: number;
  readonly onPick: (order: PreviewOrder) => void;
}) {
  if (orders.length === 0) {
    return <p className="p-4 text-muted">Заявок нет.</p>;
  }

  return (
    <table aria-label="Заявки" className="w-full border-collapse text-[13px]">
      <ListHead columns={[['Момент, UTC'], ['Сторона'], ['Исход']]} />
      <tbody>
        {orders.map((order) => (
          <ListRow
            key={order.moment}
            label={`Заявка ${formatUtc(order.moment)}`}
            selected={Number(order.moment) === selected}
            markClicks={markClicks}
            dim={order.outcome !== 'opened'}
          >
            <td className="px-4">
              <MomentButton moment={formatUtc(order.moment)} onClick={() => onPick(order)} />
            </td>
            <td className="px-4">{order.side === 'short' ? 'шорт' : 'лонг'}</td>
            <td className="px-4 text-xs">{outcomeOf(order)}</td>
          </ListRow>
        ))}
      </tbody>
    </table>
  );
}
