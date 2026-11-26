import { clockText, momentText } from './format';

// Change fix-pair-chart-and-local-time, tasks 3.2 and 3.3: the moments of the market in the zone of the browser
// (Europe/Moscow in the tests), without «UTC».
describe('the moments of the market', () => {
  it('writes them in the zone of the browser', () => {
    expect(momentText(Date.UTC(2026, 10, 22, 14, 30))).toBe('22.11 17:30');
    expect(clockText(Date.UTC(2026, 10, 22, 14, 32, 5))).toBe('17:32:05');
    expect(momentText(null)).toBe('—');
    expect(clockText(undefined)).toBe('—');
  });
});
