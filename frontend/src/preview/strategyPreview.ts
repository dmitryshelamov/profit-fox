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
          tone: short ? 'short' : 'long',
          text: short ? 'шорт' : 'лонг',
          order: moment,
        },
        {
          // The candle the exit falls in; an exit at a close belongs to the candle it closes.
          time: (Math.floor((exit - 1) / length) * length) / 1000,
          moment: exit,
          position: 'inBar',
          shape: 'circle',
          tone: 'exit',
          text: 'выход',
          order: moment,
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
    return `${reasons[order.trade.exitReason] ?? order.trade.exitReason}, ${percent(Number(order.trade.net))}`;
  }

  if (order.outcome === 'skipped') {
    return 'пропущена: позиция открыта';
  }

  if (order.outcome === 'excluded') {
    return `без сделки: ${exclusions[order.reason ?? ''] ?? order.reason ?? '—'}`;
  }

  return 'не посчитана';
}

/** A result after costs as a percent with two digits and its sign, `-0.98 %`, as the lists and the header write it. */
export function percent(net: number): string {
  const value = net * 100;
  return `${value >= 0 ? '+' : ''}${value.toFixed(2)} %`;
}

/** The orders that opened a trade (spec strategy-constructor «Вкладки предпросмотра стратегии»). */
export function tradesOf(orders: readonly PreviewOrder[]): PreviewOrder[] {
  return orders.filter((order) => order.outcome === 'opened' && order.trade);
}

/** The sum of the results of the trades after costs, or null without trades (spec «Сумма результатов предпросмотра стратегии»). */
export function totalOf(orders: readonly PreviewOrder[]): number | null {
  const trades = tradesOf(orders);
  return trades.length === 0
    ? null
    : trades.reduce((sum, order) => sum + Number(order.trade!.net), 0);
}

const exits: Record<string, string> = {
  stop: 'по стопу',
  target: 'по цели',
  time: 'по сроку',
};

/** How a trade ended and how long it was held, as «по сроку · 4h». */
export function exitOf(order: PreviewOrder): string {
  const trade = order.trade;
  if (!trade) {
    return '—';
  }

  const held = spanOf(Number(trade.exitTime) - Number(trade.entryTime));
  return `${exits[trade.exitReason] ?? trade.exitReason} · ${held}`;
}

/** A length of time in the signs of the graph format: `4h`, `8h 30m`, `1d 2h`. */
export function spanOf(ms: number): string {
  const minutes = Math.round(ms / 60_000);
  const parts = [
    [Math.floor(minutes / 1440), 'd'],
    [Math.floor((minutes % 1440) / 60), 'h'],
    [minutes % 60, 'm'],
  ] as const;
  const written = parts.filter(([count]) => count > 0).map(([count, sign]) => `${count}${sign}`);
  return written.length === 0 ? '0m' : written.join(' ');
}
