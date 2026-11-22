import { describe, expect, it } from 'vitest';

import {
  closeAt,
  formatUtc,
  historyStart,
  lengthOf,
  marksOf,
  placeOf,
  timeframes,
} from './preview';

const utc = (text: string) => Date.parse(`${text}Z`);

describe('the helpers of the preview', () => {
  // Change add-ideas-and-market, task 2.5: five timeframes and their lengths.
  it('knows five timeframes and their lengths', () => {
    expect(timeframes).toEqual(['5m', '15m', '1h', '4h', '1d']);
    expect(timeframes.map(lengthOf)).toEqual([300_000, 900_000, 3_600_000, 14_400_000, 86_400_000]);
  });

  it('puts the mark of an event on the candle that closed at its moment', () => {
    const marks = marksOf([{ moment: utc('2026-01-04T12:00') }], '4h');

    expect(marks).toEqual([
      { time: utc('2026-01-04T08:00') / 1000, moment: utc('2026-01-04T12:00') },
    ]);
  });

  it('writes moments in UTC whatever the zone of the machine', () => {
    expect(formatUtc(utc('2026-04-16T04:00'))).toBe('2026-04-16 04:00');
  });

  it('takes the start of history from the first candle, not from the covered window', () => {
    const candles = [
      { openTime: utc('2025-11-24T00:00'), open: 1, high: 1, low: 1, close: 1, volume: 1 },
      { openTime: utc('2025-11-24T04:00'), open: 1, high: 1, low: 1, close: 2, volume: 1 },
    ];

    expect(historyStart(candles)).toBe(utc('2025-11-24T00:00'));
    expect(historyStart([])).toBeNull();
    expect(closeAt(candles, utc('2025-11-24T08:00'), '4h')).toBe(2);
  });

  it('names where a problem of the graph is', () => {
    expect(
      placeOf({ code: 'UnknownNode', reason: '', wire: { from: 'rate.value', to: 'hgh.a' } }),
    ).toBe('провод rate.value → hgh.a');
    expect(placeOf({ code: 'MissingInput', reason: '', node: 'high', pin: 'a' })).toBe('high.a');
    expect(placeOf({ code: 'UnknownInstrument', reason: '', field: 'instrument' })).toBe(
      'instrument',
    );
  });
});
