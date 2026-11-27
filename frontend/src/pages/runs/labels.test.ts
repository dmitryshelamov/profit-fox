import { clock, shortMoment, zonedMoment } from './labels';

// Change fix-pair-chart-and-local-time, task 3.2: the moments of a run in the zone of the browser (Europe/Moscow in the
// tests), the zone named where no column names it.
describe('the moments of a run', () => {
  it('writes them in the zone of the browser', () => {
    expect(zonedMoment('2026-11-20T08:07:00Z')).toBe('2026-11-20 11:07 UTC+3');
    expect(zonedMoment(undefined)).toBe('—');
    expect(shortMoment('2026-11-20T21:01:00Z')).toBe('21.11 00:01');
    expect(clock('2026-11-20T21:14:00Z')).toBe('00:14');
  });
});
