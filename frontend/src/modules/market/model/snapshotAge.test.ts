import { ageText, isStale, utcClock } from './snapshotAge';

const at = Date.parse('2026-11-04T12:00:00Z');

describe('age of the snapshot', () => {
  it.each([
    [14_000, '14 с назад'],
    [59_000, '59 с назад'],
    [60_000, '1 мин назад'],
    [7 * 60_000 + 30_000, '7 мин назад'],
    [59 * 60_000, '59 мин назад'],
    [3 * 3_600_000 + 20 * 60_000, '3 ч назад'],
  ])('says %d ms as «%s»', (age, text) => {
    expect(ageText('2026-11-04T12:00:00Z', at + age)).toBe(text);
  });

  it('does not show a negative age when the clock of the browser is behind the server', () => {
    expect(ageText('2026-11-04T12:00:00Z', at - 5000)).toBe('0 с назад');
  });

  it('calls a snapshot stale once it is older than 3 minutes', () => {
    expect(isStale('2026-11-04T12:00:00Z', at + 180_000)).toBe(false);
    expect(isStale('2026-11-04T12:00:00Z', at + 181_000)).toBe(true);
  });
});

describe('utc clock', () => {
  it('shows the time of day in UTC, whatever the time zone of the browser', () => {
    expect(utcClock('2026-11-04T14:32:10+03:00')).toBe('11:32 UTC');
    expect(utcClock('2026-11-04T07:05:00Z')).toBe('07:05 UTC');
  });
});
