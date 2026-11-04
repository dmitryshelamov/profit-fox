import { ColorType, createChart } from 'lightweight-charts';

import { token } from './tokens';

/**
 * A chart of the pair page: UTC time axis, the colours of the theme, as high as a side chart needs; `priceFormatter`
 * writes the values of the price axis.
 */
export function createPairChart(
  container: HTMLElement,
  priceFormatter?: (value: number) => string,
) {
  return createChart(container, {
    ...(priceFormatter ? { localization: { priceFormatter } } : {}),
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
    timeScale: { timeVisible: true, secondsVisible: false },
  });
}
