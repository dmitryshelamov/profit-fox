import type { LinePoint } from './pairSeries';

/** A bar of the trade flow as far as the panel draws it: the turnover of the buys and of the sells in USDT. */
export interface TradeFlowInput {
  readonly openTime: string;
  readonly buyTurnover: string;
  readonly sellTurnover: string;
}

/**
 * The columns of the panel «Покупки и продажи», buys up and sells down, and the line of CVD: the running difference of buys and sells from zero at
 * the start of the bars given. Decimal strings become numbers here, for drawing only.
 */
export function tradeFlowSeries(bars: readonly TradeFlowInput[]): {
  buys: LinePoint[];
  sells: LinePoint[];
  cvd: LinePoint[];
} {
  let running = 0;

  return bars.reduce<{ buys: LinePoint[]; sells: LinePoint[]; cvd: LinePoint[] }>(
    (series, bar) => {
      const time = Date.parse(bar.openTime) / 1000;
      const buy = Number(bar.buyTurnover);
      const sell = Number(bar.sellTurnover);
      running += buy - sell;
      series.buys.push({ time, value: buy });
      series.sells.push({ time, value: -sell });
      series.cvd.push({ time, value: running });

      return series;
    },
    { buys: [], sells: [], cvd: [] },
  );
}
