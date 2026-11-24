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
