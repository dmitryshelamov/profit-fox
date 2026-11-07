import { TickMarkType } from 'lightweight-charts';

import { crosshairTime, tickMarkText } from './chartTime';

const seconds = (iso: string) => Date.parse(iso) / 1000;

// Requirement "Время в местном часовом поясе": the axis and the crosshair of a chart in the local zone (Moscow, UTC+3).
describe('time of a chart', () => {
  it.each([
    ['2025-12-31T21:00:00Z', TickMarkType.Year, '2026'],
    ['2026-10-31T21:00:00Z', TickMarkType.Month, 'ноя'],
    ['2026-11-02T22:00:00Z', TickMarkType.DayOfMonth, '3'],
    ['2026-11-02T14:05:00Z', TickMarkType.Time, '17:05'],
  ])('marks %s on the axis as %s in the local zone', (iso, type, text) => {
    expect(tickMarkText(seconds(iso), type)).toBe(text);
  });

  it('tells the moment under the crosshair in the local zone with its year', () => {
    expect(crosshairTime(seconds('2024-01-05T14:00:00Z'))).toBe('2024-01-05 17:00');
  });
});
