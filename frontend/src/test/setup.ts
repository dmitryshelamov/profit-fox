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
    ColorType: { Solid: 'solid' },
    createChart: vi.fn(() => ({
      addSeries: vi.fn(() => ({
        setData: vi.fn((data: unknown[]) => {
          drawn.candles = data;
          drawn.setData += 1;
        }),
      })),
      remove: vi.fn(),
      subscribeClick: vi.fn((handler: (param: { time?: number }) => void) => {
        drawn.click = handler;
      }),
      unsubscribeClick: vi.fn(() => {
        drawn.click = null;
      }),
      timeScale: () => ({
        fitContent: vi.fn(),
        setVisibleRange: vi.fn((range: { from: number; to: number }) => {
          drawn.range = range;
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
