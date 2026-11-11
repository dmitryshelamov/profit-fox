import { compactNumber } from '@/modules/market';
import { compareDecimals, subtractDecimals } from '@/shared/format/decimal';

/** The caption of a bar: the buys, the sells and their difference in compact USDT, as the turnovers on the page; the difference with its sign. */
export function barCaption(bar: { buyTurnover: string; sellTurnover: string }): {
  buys: string;
  sells: string;
  difference: string;
} {
  const difference = subtractDecimals(bar.buyTurnover, bar.sellTurnover);
  const sign = compareDecimals(difference, '0');

  return {
    buys: compactNumber(bar.buyTurnover),
    sells: compactNumber(bar.sellTurnover),
    difference:
      sign === 0 ? '0' : `${sign > 0 ? '+' : '−'}${compactNumber(difference.replace('-', ''))}`,
  };
}
