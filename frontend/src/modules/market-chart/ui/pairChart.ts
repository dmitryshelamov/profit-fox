import { ColorType, createChart } from 'lightweight-charts';

import { localTime } from './chartTime';
import { token } from './tokens';

/**
 * A chart of the pair page: the time in the local zone, the colours of the theme, as high as a side chart needs;
 * `priceFormatter` writes the values of the price axis.
 */
export function createPairChart(
  container: HTMLElement,
  priceFormatter?: (value: number) => string,
) {
  return createChart(container, {
    ...localTime,
    localization: { ...localTime.localization, ...(priceFormatter ? { priceFormatter } : {}) },
    height: 200,
    autoSize: true,
    layout: {
      background: { type: ColorType.Solid, color: token('--color-canvas', '#101216') },
      textColor: token('--color-muted', '#8c94a3'),
    },
    grid: {
      vertLines: { color: token('--color-line', '#2c313b') },
      horzLines: { color: token('--color-line', '#2c313b') },
    },
  });
}
