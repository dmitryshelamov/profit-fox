import { TickMarkType, type UTCTimestamp } from 'lightweight-charts';

import { crosshairTime, monthTick, priceText, timeTick } from './chartFormat';

describe('the scales of the chart', () => {
  // Change fix-pair-chart-and-local-time, task 3.4: by the month of the zone of the browser (Europe/Moscow in the tests).
  it('names a month in three Russian letters, by the zone of the browser', () => {
    expect(monthTick(Date.UTC(2025, 7, 1) / 1000)).toBe('авг');
    // 23:00 of 30 September in UTC is already 02:00 of 1 October in Moscow.
    expect(monthTick(Date.UTC(2025, 8, 30, 23) / 1000)).toBe('окт');
    expect(monthTick(Date.UTC(2026, 4, 1) / 1000)).toBe('май');
    expect(monthTick(Date.UTC(2026, 0, 1) / 1000)).toBe('янв');
  });

  // Scenario «Шкала графика» (task 3.4): the data stay in seconds of UTC, the scale and the crosshair write local time.
  it('writes the time scale and the crosshair in the zone of the browser', () => {
    const at = (ms: number) => (ms / 1000) as UTCTimestamp;
    const seven = at(Date.UTC(2026, 10, 26, 7));
    expect(timeTick(seven, TickMarkType.Time)).toBe('10:00');
    expect(timeTick(at(Date.UTC(2026, 10, 26, 7, 0, 5)), TickMarkType.TimeWithSeconds)).toBe(
      '10:00:05',
    );
    expect(timeTick(seven, TickMarkType.DayOfMonth)).toBe('26');
    expect(timeTick(at(Date.UTC(2026, 10, 30, 22)), TickMarkType.DayOfMonth)).toBe('1');
    expect(timeTick(at(Date.UTC(2026, 10, 30, 22)), TickMarkType.Month)).toBe('дек');
    expect(timeTick(at(Date.UTC(2026, 11, 31, 22)), TickMarkType.Year)).toBe('2027');
    expect(crosshairTime(seven)).toBe('2026-11-26 10:00');
  });

  it('writes a price with the digits its size needs', () => {
    expect(priceText(120000)).toBe('120\u00A0000');
    expect(priceText(79069.5)).toBe('79\u00A0070');
    expect(priceText(3456.78)).toBe('3\u00A0457');
    expect(priceText(142.5)).toBe('142,50');
    expect(priceText(1.2345)).toBe('1,2345');
    expect(priceText(0.012345)).toBe('0,012345');
    expect(priceText(0.00001234)).toBe('0,00001234');
    expect(priceText(0)).toBe('0');
  });
});
