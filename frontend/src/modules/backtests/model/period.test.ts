import { lastClosedBoundary, periodEndingAt } from './period';

const at = (iso: string) => new Date(iso);

describe('boundary of the last closed candle', () => {
  it.each([
    ['1', '2026-11-02T12:30:45Z', '2026-11-02T12:30:00Z'],
    ['3', '2026-11-02T12:30:00Z', '2026-11-02T12:30:00Z'],
    ['5', '2026-11-02T12:34:59Z', '2026-11-02T12:30:00Z'],
    ['15', '2026-11-02T12:44:00Z', '2026-11-02T12:30:00Z'],
    ['30', '2026-11-02T12:44:00Z', '2026-11-02T12:30:00Z'],
    ['60', '2026-11-02T12:30:00Z', '2026-11-02T12:00:00Z'],
    ['120', '2026-11-02T12:30:00Z', '2026-11-02T12:00:00Z'],
    ['240', '2026-11-02T12:30:00Z', '2026-11-02T12:00:00Z'],
    ['360', '2026-11-02T12:30:00Z', '2026-11-02T12:00:00Z'],
    ['720', '2026-11-02T11:59:00Z', '2026-11-02T00:00:00Z'],
    ['D', '2026-11-02T12:30:00Z', '2026-11-02T00:00:00Z'],
    ['W', '2026-11-04T12:30:00Z', '2026-11-02T00:00:00Z'],
    ['W', '2026-11-08T23:59:00Z', '2026-11-02T00:00:00Z'],
    ['M', '2026-11-18T12:30:00Z', '2026-11-01T00:00:00Z'],
  ])('interval %s at %s is %s', (interval, now, expected) => {
    expect(lastClosedBoundary(at(now), interval).toISOString()).toBe(at(expected).toISOString());
  });
});

describe('period ending at the boundary', () => {
  it('takes 30 days for the default period', () => {
    const period = periodEndingAt(at('2026-11-02T12:30:00Z'), '60', 'month');

    expect(period.from.toISOString()).toBe('2026-10-03T12:00:00.000Z');
    expect(period.to.toISOString()).toBe('2026-11-02T12:00:00.000Z');
  });

  it('takes 90 days', () => {
    const period = periodEndingAt(at('2026-11-02T12:30:00Z'), '60', 'quarter');

    expect(period.from.toISOString()).toBe('2026-08-04T12:00:00.000Z');
  });

  it('takes a year', () => {
    const period = periodEndingAt(at('2026-11-02T12:30:00Z'), 'D', 'year');

    expect(period.from.toISOString()).toBe('2025-11-02T00:00:00.000Z');
    expect(period.to.toISOString()).toBe('2026-11-02T00:00:00.000Z');
  });
});
