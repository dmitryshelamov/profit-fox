import { render } from '@testing-library/react';
import { createChart, createSeriesMarkers } from 'lightweight-charts';

import type { FillMarker, PricePoint } from '../model/pricePoint';
import { EquityComparisonChart, type EquityLine } from './EquityComparisonChart';
import { PriceEquityChart } from './PriceEquityChart';

const points: PricePoint[] = [
  {
    openTime: '2024-01-01T00:00:00Z',
    open: '100',
    high: '110',
    low: '95',
    close: '105',
    equity: '1000',
  },
];

const entry = (time: number): FillMarker => ({ time, side: 'buy', kind: 'entry', count: 1 });

const markersPlugin = (index: number) =>
  vi.mocked(createSeriesMarkers).mock.results[index]!.value as {
    setMarkers: ReturnType<typeof vi.fn>;
  };

beforeEach(() => {
  vi.mocked(createChart).mockClear();
  vi.mocked(createSeriesMarkers).mockClear();
});

describe('price and equity chart', () => {
  it('attaches one markers plugin and updates it when the markers change', () => {
    const { rerender } = render(
      <PriceEquityChart points={points} markers={[entry(1)]} label="c" />,
    );

    expect(createSeriesMarkers).toHaveBeenCalledTimes(1);
    expect(markersPlugin(0).setMarkers).toHaveBeenLastCalledWith([
      expect.objectContaining({ time: 1 }),
    ]);

    rerender(<PriceEquityChart points={points} markers={[entry(1), entry(2)]} label="c" />);

    expect(createSeriesMarkers).toHaveBeenCalledTimes(1);
    expect(markersPlugin(0).setMarkers).toHaveBeenLastCalledWith([
      expect.objectContaining({ time: 1 }),
      expect.objectContaining({ time: 2 }),
    ]);
  });
});

describe('equity comparison chart', () => {
  const line = (label: string): EquityLine => ({
    label,
    color: '--color-series-1',
    points: [{ openTime: '2024-01-01T00:00:00Z', equity: '1000' }],
  });
  const chartOf = () =>
    vi.mocked(createChart).mock.results[0]!.value as { removeSeries: ReturnType<typeof vi.fn> };

  it('takes the old lines off the chart when the lines change', () => {
    const { rerender } = render(<EquityComparisonChart lines={[line('a')]} label="c" />);

    rerender(<EquityComparisonChart lines={[line('b')]} label="c" />);

    expect(chartOf().removeSeries).toHaveBeenCalledTimes(1);
  });

  it('leaves the lines to the chart when the page goes away', () => {
    const { unmount } = render(<EquityComparisonChart lines={[line('a')]} label="c" />);

    unmount();

    expect(chartOf().removeSeries).not.toHaveBeenCalled();
  });
});
