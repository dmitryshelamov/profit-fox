import '@testing-library/jest-dom/vitest';

import { vi } from 'vitest';

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
      timeScale: () => ({ fitContent: vi.fn() }),
    })),
    createSeriesMarkers: vi.fn(() => ({
      setMarkers: vi.fn((markers: unknown[]) => {
        drawn.markers = markers;
      }),
    })),
  };
});
