import { localClock, localClockSeconds, localDateTime, localMoment, zoneLabel } from './time';

// Change fix-pair-chart-and-local-time, task 3.1: the tests run in Europe/Moscow (UTC+3), so every moment written here is
// the local time of the browser, not UTC.
describe('the moments of the bench in the zone of the browser', () => {
  const seven = Date.UTC(2026, 10, 26, 7);

  it('writes a moment of 07:00 UTC as the time of Moscow', () => {
    expect(localMoment(seven)).toBe('26.11 10:00');
    expect(localDateTime(seven)).toBe('2026-11-26 10:00');
    expect(localClock(seven)).toBe('10:00');
    expect(localClockSeconds(Date.UTC(2026, 10, 26, 14, 1, 1))).toBe('17:01:01');
  });

  it('moves to the next local day past midnight', () => {
    expect(localMoment(Date.UTC(2026, 11, 31, 22, 5))).toBe('01.01 01:05');
    expect(localDateTime(Date.UTC(2026, 11, 31, 22, 5))).toBe('2027-01-01 01:05');
  });

  it('names the zone by its offset from UTC', () => {
    const at = (offset: number) => ({ getTimezoneOffset: () => offset });
    expect(zoneLabel(at(-420))).toBe('UTC+7');
    expect(zoneLabel(at(-330))).toBe('UTC+5:30');
    expect(zoneLabel(at(180))).toBe('UTC-3');
    expect(zoneLabel(at(0))).toBe('UTC+0');
    expect(zoneLabel()).toBe('UTC+3');
  });
});
