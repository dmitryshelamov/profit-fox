import '@testing-library/jest-dom/vitest';

// jsdom does not implement scrolling; the router restores scroll position on navigation.
window.scrollTo = () => undefined;

// jsdom lays nothing out. React Flow shows a node only after measuring it, so measurements report a size and the
// resize observer reports every observed element at once (the setup React Flow's testing guide gives).
globalThis.ResizeObserver = class implements ResizeObserver {
  private readonly pending = new Set<ReturnType<typeof setTimeout>>();
  constructor(private readonly callback: ResizeObserverCallback) {}
  observe(target: Element) {
    const contentRect = { width: 1200, height: 800 } as DOMRectReadOnly;
    const timer = setTimeout(() => {
      this.pending.delete(timer);
      // A test may end before the report: an unmounted element is not reported.
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
