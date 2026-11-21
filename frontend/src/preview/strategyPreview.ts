import type { PreviewOrder } from '@/api/client';

import { type EventMark, lengthOf } from './preview';

/**
 * The marks of the trades of a strategy (spec strategy-constructor «Сделки на графике»): an entry on the candle that
 * closed at the order, an arrow by its side, and an exit on the candle the exit falls in. An order that opened no
 * trade has no marks. The chart wants them in time order.
 */
export function tradeMarks(orders: readonly PreviewOrder[], timeframe: string): EventMark[] {
  const length = lengthOf(timeframe);
  return orders
    .flatMap((order): EventMark[] => {
      const trade = order.trade;
      if (order.outcome !== 'opened' || !trade) {
        return [];
      }

      const moment = Number(order.moment);
      const exit = Number(trade.exitTime);
      const short = order.side === 'short';
      return [
        {
          time: (moment - length) / 1000,
          moment,
          position: short ? 'aboveBar' : 'belowBar',
          shape: short ? 'arrowDown' : 'arrowUp',
          color: short ? '#f97316' : '#38bdf8',
          text: short ? 'шорт' : 'лонг',
        },
        {
          // The candle the exit falls in; an exit at a close belongs to the candle it closes.
          time: (Math.floor((exit - 1) / length) * length) / 1000,
          moment: exit,
          position: 'inBar',
          shape: 'circle',
          color: '#e4e4e7',
          text: 'выход',
        },
      ];
    })
    .sort((a, b) => a.time - b.time);
}

const reasons: Record<string, string> = {
  stop: 'стоп',
  target: 'цель',
  time: 'срок',
};

const exclusions: Record<string, string> = {
  plan: 'нет плана сделки',
  coverage: 'нет минутной истории',
  'no-trade': 'не было торгов',
  funding: 'нет истории фандинга',
  period: 'срок за концом разведки',
};

/** What became of an order, in words: the exit and the result of its trade, or why there is none. */
export function outcomeOf(order: PreviewOrder): string {
  if (order.outcome === 'opened' && order.trade) {
    const net = Number(order.trade.net) * 100;
    return `${reasons[order.trade.exitReason] ?? order.trade.exitReason}, ${net >= 0 ? '+' : ''}${net.toFixed(2)} %`;
  }

  if (order.outcome === 'skipped') {
    return 'пропущена: позиция открыта';
  }

  if (order.outcome === 'excluded') {
    return `без сделки: ${exclusions[order.reason ?? ''] ?? order.reason ?? '—'}`;
  }

  return 'не посчитана';
}
