import type {
  IPrimitivePaneRenderer,
  ISeriesApi,
  ISeriesPrimitive,
  SeriesType,
  Time,
} from 'lightweight-charts';

import { bandRect, type ZoneBand } from '../model/ideaOverlay';
import { token, withAlpha } from './tokens';

const alpha = 0.16;

const colourOf = (kind: string) =>
  kind === 'support'
    ? token('--color-ok', '#3fb37f')
    : kind === 'resistance'
      ? token('--color-err', '#e5484d')
      : token('--color-warn', '#e0a43a');

/**
 * Support and resistance zones as horizontal bands across the whole pane, behind the candles: a series primitive, which
 * the library asks to draw on its canvas. A band is placed from its prices on every draw, so it follows the price scale.
 */
export class ZoneBandsPrimitive implements ISeriesPrimitive<Time> {
  private bands: readonly ZoneBand[] = [];
  private series: ISeriesApi<SeriesType, Time> | null = null;
  private requestUpdate: (() => void) | null = null;

  attached(param: { series: ISeriesApi<SeriesType, Time>; requestUpdate: () => void }) {
    this.series = param.series;
    this.requestUpdate = param.requestUpdate;
  }

  detached() {
    this.series = null;
    this.requestUpdate = null;
  }

  setBands(bands: readonly ZoneBand[]) {
    this.bands = bands;
    this.requestUpdate?.();
  }

  paneViews() {
    const draw: IPrimitivePaneRenderer['draw'] = (target) =>
      target.useBitmapCoordinateSpace(({ context, bitmapSize, verticalPixelRatio }) => {
        const series = this.series;

        if (!series) {
          return;
        }

        for (const band of this.bands) {
          const rect = bandRect(band, (price) => series.priceToCoordinate(price));

          if (rect) {
            context.fillStyle = withAlpha(colourOf(band.kind), alpha);
            context.fillRect(
              0,
              rect.top * verticalPixelRatio,
              bitmapSize.width,
              rect.height * verticalPixelRatio,
            );
          }
        }
      });

    return [{ zOrder: () => 'bottom' as const, renderer: () => ({ draw }) }];
  }
}
