import type { PreviewCycle, PreviewFill, PreviewOrder, StrategyPreview } from '@/api/client';

import { type EventMark, lengthOf } from './preview';

/** A counted cycle with the moment of the order that opened it, the key that selects it in the lists. */
export interface ShownCycle {
  readonly cycle: PreviewCycle;
  readonly key: number;
}

/**
 * The cycles to show (spec strategy-constructor «Вкладки предпросмотра стратегии»): those not left out on a hole in the
 * history, each keyed by the moment of the order that opened it — or of its first fill when no order names it.
 */
export function cyclesOf(preview: Pick<StrategyPreview, 'orders' | 'cycles'>): ShownCycle[] {
  return preview.cycles.flatMap((cycle, index): ShownCycle[] => {
    if (cycle.exclusion !== null) {
      return [];
    }

    const opening = preview.orders.find(
      (order) => order.outcome === 'filled' && Number(order.cycle) === index,
    );
    return [{ cycle, key: Number(opening?.moment ?? cycle.fills[0]?.moment ?? 0) }];
  });
}

/**
 * The marks of the cycles of a strategy (spec strategy-constructor «Сделки на графике», «Докупки на графике»): one per
 * fill — the entry on the candle that closed at its order, an arrow by the side; an averaging buy as an arrow of the
 * side; a decrease as a square and the exit as a circle, on the candle they fall in, an exit at a close on the candle it
 * closes. A skipped order has no marks. The chart wants them in time order.
 */
export function tradeMarks(
  preview: Pick<StrategyPreview, 'orders' | 'cycles' | 'timeframe'>,
): EventMark[] {
  const length = lengthOf(preview.timeframe);
  return cyclesOf(preview)
    .flatMap(({ cycle, key }) =>
      cycle.fills.map((fill) => markOf(fill, cycle.side === 'short', key, length)),
    )
    .sort((a, b) => a.time - b.time);
}

function markOf(fill: PreviewFill, short: boolean, key: number, length: number): EventMark {
  const moment = Number(fill.moment);
  switch (fill.action) {
    case 'entry':
    case 'increase': {
      // The entry of an order placed at a close is on the candle that closed at it, as the event of its signal; any
      // other fill is on the candle it falls in.
      const atClose = fill.action === 'entry' && moment === key && moment % length === 0;
      const time = atClose ? moment - length : Math.floor(moment / length) * length;
      return {
        time: time / 1000,
        moment,
        position: short ? 'aboveBar' : 'belowBar',
        shape: short ? 'arrowDown' : 'arrowUp',
        tone: short ? 'short' : 'long',
        text: fill.action === 'entry' ? (short ? 'шорт' : 'лонг') : 'докупка',
        order: key,
      };
    }
    default:
      return {
        // An exit at a close belongs to the candle it closes.
        time: (Math.floor((moment - 1) / length) * length) / 1000,
        moment,
        position: 'inBar',
        shape: fill.action === 'exit' ? 'circle' : 'square',
        tone: 'exit',
        text: fill.action === 'exit' ? 'выход' : 'уменьшение',
        order: key,
      };
  }
}

const reasons: Record<string, string> = {
  stop: 'стоп',
  target: 'цель',
  time: 'срок',
  limit: 'лимитка',
  market: 'по рынку',
  close: 'закрытие',
};

const exclusions: Record<string, string> = {
  plan: 'нет плана сделки',
  coverage: 'нет минутной истории',
  'no-trade': 'не было торгов',
  funding: 'нет истории фандинга',
  period: 'срок за концом разведки',
};

/** The orders that opened their cycles: the first filled order of each cycle. */
export function openersOf(orders: readonly PreviewOrder[]): Set<PreviewOrder> {
  const seen = new Set<number>();
  return new Set(
    orders.filter((order) => {
      if (order.outcome !== 'filled' || order.cycle === null || seen.has(Number(order.cycle))) {
        return false;
      }

      seen.add(Number(order.cycle));
      return true;
    }),
  );
}

/**
 * What became of an order, in words: for an order that opened a cycle, how the cycle ended and its result; for another
 * fill, its cycle; skipped, without a trade and why, or not computed.
 */
export function outcomeOf(
  order: PreviewOrder,
  cycles: readonly PreviewCycle[],
  opened: boolean,
): string {
  if (order.outcome === 'filled') {
    const cycle = order.cycle === null ? undefined : cycles[Number(order.cycle)];
    if (cycle !== undefined && opened) {
      return cycle.open || cycle.net === null
        ? 'исполнена, цикл открыт'
        : `${reasons[cycle.exitReason ?? ''] ?? cycle.exitReason ?? '—'}, ${percent(Number(cycle.net))}`;
    }

    return `исполнена${order.cycle === null ? '' : `, цикл ${Number(order.cycle) + 1}`}`;
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

/** An amount of USDT with two digits and its sign, `-1.54 USDT`. */
export function usdt(amount: number): string {
  return `${amount >= 0 ? '+' : ''}${amount.toFixed(2)} USDT`;
}

/** A price as the lists write it: up to six significant digits after the point. */
export function priceText(price: number): string {
  return Number(price.toPrecision(8)).toString();
}

/**
 * The sum of the results of the closed cycles after costs, as a fraction and in USDT, or null without one (spec
 * «Сумма результатов предпросмотра стратегии»): an open cycle is not in it.
 */
export function totalOf(cycles: readonly ShownCycle[]): { net: number; amount: number } | null {
  const closed = cycles.filter(({ cycle }) => !cycle.open && cycle.net !== null);
  return closed.length === 0
    ? null
    : {
        net: closed.reduce((sum, { cycle }) => sum + Number(cycle.net), 0),
        amount: closed.reduce((sum, { cycle }) => sum + Number(cycle.netAmount ?? 0), 0),
      };
}

const exits: Record<string, string> = {
  stop: 'по стопу',
  target: 'по цели',
  time: 'по сроку',
  limit: 'лимиткой',
  market: 'по рынку',
  close: 'по условию',
};

/** How a cycle ended and how long it was held, as «по сроку · 4h», or that it is open. */
export function exitOf(cycle: PreviewCycle): string {
  if (cycle.open) {
    return 'открыт';
  }

  const entry = cycle.fills[0];
  const exit = cycle.fills[cycle.fills.length - 1];
  if (!entry || !exit || cycle.exitReason === null) {
    return '—';
  }

  const held = spanOf(Number(exit.moment) - Number(entry.moment));
  return `${exits[cycle.exitReason] ?? cycle.exitReason} · ${held}`;
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
