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

/** The room kept between a label and the right edge of the area of the chart, in pixels. */
const edge = 4;

/** The height of a line of the labels, in pixels: 11 pixels of the font and a little air (task 20.2). */
export const labelHeight = 14;

/** A label at the end of its line, in pixels of the area of the chart. */
export interface LabelAt {
  readonly text: string;
  readonly x: number;
  readonly y: number;
}

/**
 * Where the labels are written (task 20.2): to the right of the end of the line, but not past the right edge of the area
 * of the chart; a label that would meet one above it — closer than `labelHeight` and across the same pixels — goes down
 * under it. The places are in the order of `labels`.
 */
export function layoutLabels(
  labels: readonly LabelAt[],
  width: number,
  measure: (text: string) => number,
): { x: number; y: number }[] {
  const boxes = labels.map((label) => {
    const size = measure(label.text);
    const x = Math.max(0, Math.min(label.x + gap, width - size - edge));
    return { x, right: x + size, y: label.y };
  });
  const placed: { x: number; right: number; y: number }[] = [];
  for (const index of boxes.map((_, at) => at).sort((a, b) => boxes[a]!.y - boxes[b]!.y)) {
    const box = boxes[index]!;
    for (const other of placed) {
      if (other.x < box.right && box.x < other.right && box.y - other.y < labelHeight) {
        box.y = other.y + labelHeight;
      }
    }

    placed.push(box);
    placed.sort((a, b) => a.y - b.y);
  }

  return boxes.map((box) => ({ x: box.x, y: box.y }));
}

/**
 * The labels of a line of an open idea written to the right of its end (task 19.14), as the mockup does: «вход 236.10»,
 * «срок 17:00». The price scale shows none of them, so they do not crowd its right edge. The places are laid out over the
 * labels of all the lines (`all`), so the labels of close levels of different lines move apart (task 20.2); this one writes
 * its own `labels`, which are some of `all`.
 */
export class LineLabels implements ISeriesPrimitive<Time> {
  private chartOf: SeriesAttachedParameter<Time> | null = null;

  constructor(
    /** The title of the line: the text of its first label. */
    readonly title: string,
    readonly labels: readonly LineLabel[],
    private readonly color: string,
    private readonly all: readonly LineLabel[] = labels,
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
        target.useMediaCoordinateSpace(({ context, mediaSize }) => {
          context.font = '11px ui-monospace, SFMono-Regular, Menlo, monospace';
          context.textBaseline = 'middle';
          context.fillStyle = this.color;
          const shown = this.all.flatMap((label) => {
            const x = attached.chart
              .timeScale()
              .timeToCoordinate((label.time / 1000) as UTCTimestamp);
            const y = attached.series.priceToCoordinate(label.price);
            return x === null || y === null ? [] : [{ label, text: label.text, x, y }];
          });
          const places = layoutLabels(
            shown,
            mediaSize.width,
            (text) => context.measureText(text).width,
          );
          shown.forEach((item, index) => {
            if (this.labels.includes(item.label)) {
              const place = places[index]!;
              context.fillText(item.text, place.x, place.y);
            }
          });
        });
      },
    };
  }
}
