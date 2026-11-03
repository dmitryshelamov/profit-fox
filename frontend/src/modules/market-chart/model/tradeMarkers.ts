/** A fill of the bot: UTC time, side and the exact price. */
export interface FillInput {
  readonly at: string;
  readonly side: 'buy' | 'sell';
  readonly price: string;
}

/** A fill as the chart draws it: the tip of the arrow stands at `price`, outside the candle on the side it points from. */
export interface TradeMarker {
  readonly time: number;
  readonly position: 'atPriceTop' | 'atPriceBottom';
  readonly shape: 'arrowUp' | 'arrowDown';
  readonly price: number;
  /** `×N` on the last fill of a side on a candle when there are several, empty otherwise. */
  readonly text: string;
}

/**
 * Places each fill on the drawn candle whose period contains it, in time order as the chart library requires. A fill
 * outside every drawn candle (before the first, in a gap, after the last one ends; `candleEnd` gives the end of a candle) is left out. A buy points up from
 * below its price, a sell down from above it.
 */
export function tradeMarkers(
  candles: readonly { readonly time: number }[],
  fills: readonly FillInput[],
  candleEnd: (openSeconds: number) => number,
): TradeMarker[] {
  const placed = fills.flatMap((fill) => {
    const at = Date.parse(fill.at) / 1000;
    const candle = candles.findLast(({ time }) => time <= at);

    return candle && at < candleEnd(candle.time) ? [{ fill, at, time: candle.time }] : [];
  });
  placed.sort((left, right) => left.time - right.time || left.at - right.at);

  const counts = new Map<string, number>();
  for (const { fill, time } of placed) {
    const key = `${time}:${fill.side}`;
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }

  const seen = new Map<string, number>();

  return placed.map(({ fill, time }) => {
    const key = `${time}:${fill.side}`;
    const number = (seen.get(key) ?? 0) + 1;
    seen.set(key, number);
    const count = counts.get(key) ?? 1;

    return {
      time,
      position: fill.side === 'buy' ? 'atPriceTop' : 'atPriceBottom',
      shape: fill.side === 'buy' ? 'arrowUp' : 'arrowDown',
      price: Number(fill.price),
      text: count > 1 && number === count ? `×${count}` : '',
    };
  });
}
