import type {
  IPrimitivePaneRenderer,
  IPrimitivePaneView,
  ISeriesPrimitive,
  SeriesAttachedParameter,
  Time,
  UTCTimestamp,
} from 'lightweight-charts';

import type { LineLabel } from './pairSeries';

/** The gap between the end of a line and its label, in pixels. */
const gap = 6;

/**
 * The labels of a line of an open idea written to the right of its end (task 19.14), as the mockup does: «вход 236.10»,
 * «срок 17:00». The price scale shows none of them, so they do not crowd its right edge.
 */
export class LineLabels implements ISeriesPrimitive<Time> {
  private chartOf: SeriesAttachedParameter<Time> | null = null;

  constructor(
    /** The title of the line: the text of its first label. */
    readonly title: string,
    readonly labels: readonly LineLabel[],
    private readonly color: string,
  ) {}

  attached(param: SeriesAttachedParameter<Time>): void {
    this.chartOf = param;
  }

  detached(): void {
    this.chartOf = null;
  }

  paneViews(): readonly IPrimitivePaneView[] {
    return [{ zOrder: () => 'top', renderer: () => this.renderer() }];
  }

  private renderer(): IPrimitivePaneRenderer | null {
    const attached = this.chartOf;
    if (attached === null) {
      return null;
    }

    return {
      draw: (target) => {
        target.useMediaCoordinateSpace(({ context }) => {
          context.font = '11px ui-monospace, SFMono-Regular, Menlo, monospace';
          context.textBaseline = 'middle';
          context.fillStyle = this.color;
          for (const label of this.labels) {
            const x = attached.chart
              .timeScale()
              .timeToCoordinate((label.time / 1000) as UTCTimestamp);
            const y = attached.series.priceToCoordinate(label.price);
            if (x !== null && y !== null) {
              context.fillText(label.text, x + gap, y);
            }
          }
        });
      },
    };
  }
}
