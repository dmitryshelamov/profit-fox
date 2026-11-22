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

// jsdom has no canvas, so the chart library is replaced by a series that keeps the data it was given.
vi.mock('lightweight-charts', async () => {
  const { drawn } = await import('./chart');
  return {
    CandlestickSeries: 'Candlestick',
    LineSeries: 'Line',
    HistogramSeries: 'Histogram',
    ColorType: { Solid: 'solid' },
    LineStyle: { Solid: 0, Dotted: 1, Dashed: 2, LargeDashed: 3, SparseDotted: 4 },
    TickMarkType: { Year: 0, Month: 1, DayOfMonth: 2, Time: 3, TimeWithSeconds: 4 },
    createChart: vi.fn(() => ({
      addSeries: vi.fn((type: string) => ({
        setData: vi.fn((data: unknown[]) => {
          drawn.series[type] = data;
          if (type === 'Candlestick') {
            drawn.candles = data;
            drawn.setData += 1;
          }
        }),
        update: vi.fn((bar: unknown) => {
          drawn.updates.push(bar);
        }),
        createPriceLine: vi.fn((line: { price: number; title?: string }) => {
          drawn.lines.push(line);
          return line;
        }),
        removePriceLine: vi.fn((line: unknown) => {
          drawn.lines = drawn.lines.filter((candidate) => candidate !== line);
        }),
      })),
      panes: () => [{ setHeight: vi.fn() }, { setHeight: vi.fn() }, { setHeight: vi.fn() }],
      remove: vi.fn(),
      subscribeClick: vi.fn((handler: (param: { time?: number }) => void) => {
        drawn.click = handler;
      }),
      unsubscribeClick: vi.fn(() => {
        drawn.click = null;
      }),
      timeScale: () => ({
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
    })),
    createSeriesMarkers: vi.fn(() => ({
      setMarkers: vi.fn((markers: unknown[]) => {
        drawn.markers = markers;
      }),
    })),
  };
});
