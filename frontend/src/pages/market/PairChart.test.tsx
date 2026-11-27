import { render } from '@testing-library/react';
import { StrictMode } from 'react';

import { drawn, nextFrame, resetDrawn } from '@/test/chart';

import { PairChart } from './PairChart';

const hour = 3_600_000;
const fourteen = Date.UTC(2026, 10, 22, 14);
const candles = Array.from({ length: 100 }, (_, index) => ({
  openTime: fourteen - (100 - index) * hour,
  open: 235,
  high: 237,
  low: 234,
  close: 236,
  volume: 10,
}));

beforeEach(() => resetDrawn());

// Task 20.2, seen in development in Chromium: under `StrictMode` the chart is drawn, removed and drawn again; the room to
// the right of the last candle goes to the chart that stays, not only to the one removed.
describe('the chart of a pair under StrictMode', () => {
  it('gives the room to the right of the last candle to the chart drawn again', async () => {
    render(
      <StrictMode>
        <PairChart
          candles={candles}
          live={null}
          interest={[]}
          funding={[]}
          marks={[]}
          selected={null}
          lines={[]}
          onEdge={() => undefined}
          onPick={() => undefined}
          timeframe="1h"
        />
      </StrictMode>,
    );
    await nextFrame();

    expect(drawn.rightOffset).toBe(10);
    expect(drawn.logicalRange?.to).toBe(99 + 10);
  });
});

// Change fix-pair-chart-and-local-time, task 3.4, scenario «Шкала графика»: the chart writes its time scale and its
// crosshair in the zone of the browser (Europe/Moscow in the tests); its data stay in seconds of UTC.
describe('the time of the chart of a pair', () => {
  it('writes the candle of 07:00 UTC as 10:00 on the scale and in the crosshair', () => {
    render(
      <PairChart
        candles={candles}
        live={null}
        interest={[]}
        funding={[]}
        marks={[]}
        selected={null}
        lines={[]}
        onEdge={() => undefined}
        onPick={() => undefined}
        timeframe="1h"
      />,
    );

    const seven = Date.UTC(2026, 10, 26, 7) / 1000;
    const options = drawn.options as {
      localization?: { timeFormatter?: (time: number) => string };
      timeScale?: { tickMarkFormatter?: (time: number, type: number) => string | null };
    };
    expect(options.timeScale?.tickMarkFormatter?.(seven, 3)).toBe('10:00');
    expect(options.localization?.timeFormatter?.(seven)).toBe('2026-11-26 10:00');
    expect((drawn.candles.at(-1) as { time: number }).time).toBe((fourteen - hour) / 1000);
  });
});
