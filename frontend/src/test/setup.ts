import '@testing-library/jest-dom/vitest';

import { afterEach, vi } from 'vitest';

// A draft or a remembered pair must not leak from one test into the next.
afterEach(() => localStorage.clear());

// jsdom does not implement scrolling; the router restores scroll position on navigation.
window.scrollTo = () => undefined;

// jsdom lays nothing out. React Flow shows a node only after measuring it, so measurements report a size and the
// resize observer reports every observed element at once (the setup of React Flow's testing guide).
globalThis.ResizeObserver = class implements ResizeObserver {
  private readonly pending = new Set<ReturnType<typeof setTimeout>>();
  constructor(private readonly callback: ResizeObserverCallback) {}
  observe(target: Element) {
    const contentRect = { width: 1200, height: 800 } as DOMRectReadOnly;
    const timer = setTimeout(() => {
      this.pending.delete(timer);
      if (target.isConnected) {
        this.callback([{ target, contentRect } as ResizeObserverEntry], this);
      }
    });
    this.pending.add(timer);
  }
  unobserve() {}
  disconnect() {
    this.pending.forEach(clearTimeout);
    this.pending.clear();
  }
};

globalThis.DOMMatrixReadOnly ??= class {
  readonly m22: number;
  constructor(transform?: string) {
    const scale = /scale\(([\d.]+)\)/.exec(transform ?? '')?.[1];
    this.m22 = scale === undefined ? 1 : Number(scale);
  }
} as unknown as typeof DOMMatrixReadOnly;

Object.defineProperties(HTMLElement.prototype, {
  offsetHeight: {
    get(this: HTMLElement) {
      return parseFloat(this.style.height) || 100;
    },
  },
  offsetWidth: {
    get(this: HTMLElement) {
      return parseFloat(this.style.width) || 160;
    },
  },
});

// jsdom does not implement the modal state of <dialog>: opening and closing set and clear its `open` attribute.
HTMLDialogElement.prototype.showModal = function (this: HTMLDialogElement) {
  this.setAttribute('open', '');
};
HTMLDialogElement.prototype.close = function (this: HTMLDialogElement) {
  this.removeAttribute('open');
};

// jsdom has no canvas, so the chart library is replaced by a series that keeps the data it was given. Like the real
// library (its `ensureDefined`), the fake chart and everything it gave — series, panes, time scale, marks — throw on any
// call after `remove()` (task 19.1).
vi.mock('lightweight-charts', async () => {
  const { drawn } = await import('./chart');
  /** The bars in view of a chart nobody scrolled or zoomed: about its width over the spacing of its bars. */
  const defaultBarsInView = 150;
  /** The methods of `target` that throw once `removed()` is true; what a method returns is guarded the same way. */
  const guarded = <T extends object>(target: T, removed: () => boolean): T =>
    new Proxy(target, {
      get(object, key, receiver) {
        const value = Reflect.get(object, key, receiver) as unknown;
        if (typeof value !== 'function') {
          return value;
        }

        return (...args: unknown[]) => {
          if (removed()) {
            throw new Error(`Value is undefined: ${String(key)} on a removed chart.`);
          }

          const result = (value as (...rest: unknown[]) => unknown).apply(object, args);
          return typeof result === 'object' && result !== null ? guarded(result, removed) : result;
        };
      },
    });
  return {
    CandlestickSeries: 'Candlestick',
    LineSeries: 'Line',
    HistogramSeries: 'Histogram',
    ColorType: { Solid: 'solid' },
    LineStyle: { Solid: 0, Dotted: 1, Dashed: 2, LargeDashed: 3, SparseDotted: 4 },
    TickMarkType: { Year: 0, Month: 1, DayOfMonth: 2, Time: 3, TimeWithSeconds: 4 },
    createChart: vi.fn((_container: unknown, chartOptions?: Record<string, unknown>) => {
      let removed = false;
      const isRemoved = () => removed;
      drawn.options = chartOptions ?? {};
      // The time scale as the library keeps it (tasks 20.1, 20.2): the points of time of the data of every series, empty
      // ones (whitespace) included; its last bar (the base) is the last point with a value of any series; the visible
      // logical range (`drawn.logicalRange`) is by the indexes of the points, and the library keeps its right edge as an
      // offset from the base. A new chart has no range and no offset given. When the base moves right with new points,
      // the range moves with it only when the last bar was in view and points were added; otherwise the library shifts the
      // offset back so the range stays (points added on the left, or a value put on a point that was there empty). A base
      // that moves left takes the range with it. A `rightOffset` applied puts the right edge that many bars past the base.
      drawn.logicalRange = null;
      drawn.rightOffset = null;
      const rows = new Map<object, { time: number; empty: boolean }[]>();
      const points = () =>
        [...new Set([...rows.values()].flat().map((point) => point.time))].sort((a, b) => a - b);
      const baseOf = (at: readonly number[]) => {
        const filled = [...rows.values()].flat().filter((point) => !point.empty);
        return filled.length === 0
          ? null
          : at.indexOf(Math.max(...filled.map((point) => point.time)));
      };
      const change = (apply: () => void) => {
        const before = points();
        const oldBase = baseOf(before);
        apply();
        const after = points();
        const newBase = baseOf(after);
        const range = drawn.logicalRange;
        if (newBase === null) {
          return;
        }

        if (range === null || oldBase === null) {
          const to = newBase + (drawn.rightOffset ?? 0);
          drawn.logicalRange = range ?? { from: to - defaultBarsInView, to };
          return;
        }

        let offset = range.to - oldBase;
        const leftShift = before.length > 0 && after[0]! < before[0]!;
        const added = after.length !== before.length;
        const lastInView = range.from <= oldBase && oldBase <= range.to;
        if (newBase > oldBase && !leftShift && (!lastInView || !added)) {
          offset -= newBase - oldBase;
        }

        drawn.logicalRange = {
          from: newBase + offset - (range.to - range.from),
          to: newBase + offset,
        };
      };
      const rowOf = (data: readonly unknown[]) =>
        data.map((item) => {
          const point = item as { time: number; value?: number; close?: number };
          return {
            time: point.time,
            empty: point.value === undefined && point.close === undefined,
          };
        });
      // The panes of the chart, the same on every call, with the stretch each was given (task 19.14).
      const panes = [0, 1, 2].map((index) =>
        guarded(
          {
            setHeight: vi.fn(),
            setStretchFactor: vi.fn((factor: number) => {
              drawn.paneFactors[index] = factor;
            }),
          },
          isRemoved,
        ),
      );
      const chart = {
        addSeries: vi.fn(
          (
            type: string,
            options?: {
              title?: string;
              color?: string;
              crosshairMarkerVisible?: boolean;
              visible?: boolean;
            },
          ) => {
            // A series not shown holds the empty points of the time scale after the last candle (task 20.2).
            if (options?.visible === false) {
              const slots = {
                setData: vi.fn((data: { time: number }[]) => {
                  change(() => rows.set(slots, rowOf(data)));
                  drawn.slots = data;
                }),
              };
              return slots;
            }

            // A line without the marker of the crosshair is a line of an open idea: kept apart from the series of the panels;
            // its title is that of the labels written at its end (task 19.14).
            const segment =
              options?.title !== undefined || options?.crosshairMarkerVisible === false
                ? {
                    title: options.title ?? '',
                    color: options.color,
                    data: [] as unknown[],
                    labels: [] as { text: string; price: number; time: number; kind: string }[],
                  }
                : null;
            if (segment !== null) {
              drawn.segments.push(segment);
            } else {
              drawn.seriesOptions[type] = { ...drawn.seriesOptions[type], ...options };
            }

            const series = {
              segment,
              // The chart of the series: marks created on it are removed with it.
              owner: { isRemoved },
              setData: vi.fn((data: unknown[]) => {
                change(() => rows.set(series, rowOf(data)));
                if (segment !== null) {
                  segment.data = data;
                  return;
                }

                drawn.series[type] = data;
                if (type === 'Candlestick') {
                  drawn.candles = data;
                  drawn.setData += 1;
                }
              }),
              update: vi.fn((bar: { time: number }) => {
                drawn.updates.push(bar);
                change(() => {
                  const row = rows.get(series) ?? [];
                  rows.set(series, [
                    ...row.filter((point) => point.time !== bar.time),
                    ...rowOf([bar]),
                  ]);
                });
              }),
              createPriceLine: vi.fn((line: { price: number; title?: string }) => {
                drawn.lines.push(line);
                return line;
              }),
              removePriceLine: vi.fn((line: unknown) => {
                drawn.lines = drawn.lines.filter((candidate) => candidate !== line);
              }),
              priceScale: () => ({
                applyOptions: vi.fn((next: { scaleMargins?: unknown }) => {
                  drawn.scaleMargins[type] = next.scaleMargins;
                }),
              }),
              applyOptions: vi.fn((next: Record<string, unknown>) => {
                if (segment === null) {
                  drawn.seriesOptions[type] = { ...drawn.seriesOptions[type], ...next };
                }
              }),
              attachPrimitive: vi.fn(
                (primitive: {
                  title?: string;
                  labels?: { text: string; price: number; time: number; kind: string }[];
                }) => {
                  if (segment !== null) {
                    segment.title = primitive.title ?? segment.title;
                    segment.labels = primitive.labels ?? [];
                  }
                },
              ),
              detachPrimitive: vi.fn(() => {
                if (segment !== null) {
                  segment.labels = [];
                }
              }),
            };
            return series;
          },
        ),
        removeSeries: vi.fn((series: { segment: unknown }) => {
          change(() => rows.delete(series));
          drawn.segments = drawn.segments.filter((candidate) => candidate !== series.segment);
          if (drawn.segments.length === 0) {
            drawn.segmentMarkers = [];
          }
        }),
        panes: () => panes,
        remove: vi.fn(() => {
          removed = true;
        }),
        subscribeClick: vi.fn((handler: (param: { time?: number }) => void) => {
          drawn.click = handler;
        }),
        unsubscribeClick: vi.fn(() => {
          drawn.click = null;
        }),
        timeScale: () => ({
          applyOptions: vi.fn((next: { rightOffset?: number }) => {
            if (next.rightOffset !== undefined) {
              drawn.rightOffset = next.rightOffset;
              const base = baseOf(points());
              if (base !== null) {
                const width =
                  drawn.logicalRange === null
                    ? defaultBarsInView
                    : drawn.logicalRange.to - drawn.logicalRange.from;
                const to = base + next.rightOffset;
                drawn.logicalRange = { from: to - width, to };
              }
            }
          }),
          // How far the right edge of the range is from the base, in bars: the offset while it stands at the edge.
          scrollPosition: vi.fn(() => {
            const base = baseOf(points());
            return drawn.logicalRange === null || base === null
              ? (drawn.rightOffset ?? 0)
              : drawn.logicalRange.to - base;
          }),
          timeToIndex: vi.fn((time: number, findNearest?: boolean) => {
            const at = points();
            const index = at.indexOf(time);
            if (index >= 0 || findNearest !== true || at.length === 0) {
              return index >= 0 ? index : null;
            }

            const after = at.findIndex((point) => point > time);
            return after < 0 ? at.length - 1 : after;
          }),
          options: () => ({ barSpacing: 6 }),
          fitContent: vi.fn(() => {
            drawn.view = 'content';
          }),
          setVisibleRange: vi.fn((range: { from: number; to: number }) => {
            drawn.range = range;
            drawn.view = 'range';
          }),
          getVisibleLogicalRange: vi.fn(() => drawn.logicalRange),
          setVisibleLogicalRange: vi.fn((range: { from: number; to: number }) => {
            drawn.logicalRange = range;
          }),
          subscribeVisibleLogicalRangeChange: vi.fn(
            (handler: (range: { from: number; to: number } | null) => void) => {
              drawn.rangeChange = handler;
            },
          ),
          unsubscribeVisibleLogicalRangeChange: vi.fn(() => {
            drawn.rangeChange = null;
          }),
        }),
      };
      return guarded(chart, isRemoved);
    }),
    createSeriesMarkers: vi.fn(
      (
        series: { segment?: unknown; owner?: { isRemoved: () => boolean } },
        initial: unknown[] = [],
      ) => {
        const removed = series.owner?.isRemoved ?? (() => false);
        if (removed()) {
          throw new Error(
            'Value is undefined: createSeriesMarkers on a series of a removed chart.',
          );
        }

        const onSegment = series.segment !== null && series.segment !== undefined;
        if (onSegment) {
          drawn.segmentMarkers = [...drawn.segmentMarkers, ...initial];
        }

        return guarded(
          {
            setMarkers: vi.fn((markers: unknown[]) => {
              if (onSegment) {
                drawn.segmentMarkers = markers;
                return;
              }

              drawn.markers = markers;
            }),
          },
          removed,
        );
      },
    ),
  };
});
