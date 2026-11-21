import type { PreviewOrder } from '@/api/client';

import { formatUtc } from './preview';
import { outcomeOf } from './strategyPreview';

/**
 * The orders of a strategy in time order (spec strategy-constructor «Панель предпросмотра стратегии»): the moment in
 * UTC, the side and the outcome — the exit and the result after costs, skipped, without a trade and why, or not
 * computed. A click on a moment shows the order on the chart.
 */
export function OrderList({
  orders,
  onPick,
}: {
  readonly orders: readonly PreviewOrder[];
  readonly onPick: (order: PreviewOrder) => void;
}) {
  if (orders.length === 0) {
    return <p className="text-sm text-zinc-400">Заявок нет.</p>;
  }

  return (
    <table aria-label="Заявки" className="w-full text-sm">
      <thead className="text-left text-zinc-400">
        <tr>
          <th className="py-1 font-normal">Момент, UTC</th>
          <th className="py-1 font-normal">Сторона</th>
          <th className="py-1 font-normal">Исход</th>
        </tr>
      </thead>
      <tbody className="font-mono">
        {orders.map((order) => (
          <tr
            key={order.moment}
            aria-label={`Заявка ${formatUtc(order.moment)}`}
            className={`border-t border-zinc-800 ${order.outcome === 'opened' ? '' : 'text-zinc-500'}`}
          >
            <td className="py-1">
              <button type="button" className="hover:text-amber-300" onClick={() => onPick(order)}>
                {formatUtc(order.moment)}
              </button>
            </td>
            <td className="py-1">{order.side === 'short' ? 'шорт' : 'лонг'}</td>
            <td className="py-1 font-sans">{outcomeOf(order)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
