import { candlesOfHours, durationText, overfitWarning, sameRule } from './exitRules';

const grid15 = [1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384, 512, 672];

describe('candlesOfHours', () => {
  // Scenario "Ввод времени": 10 hours are 40 candles of 15 minutes, 48 on the grid.
  it('rounds a time up to the grid and says so', () => {
    expect(candlesOfHours('10', '15', grid15)).toEqual({
      candles: 48,
      note: 'введено 10 ч — округлено до 48 свечей',
    });
  });

  it('takes a time on the grid as it is', () => {
    expect(candlesOfHours('12', '15', grid15)).toEqual({ candles: 48, note: null });
    expect(candlesOfHours('0.25', '15', grid15)).toEqual({ candles: 1, note: null });
  });

  it('takes 7 days for a longer time', () => {
    expect(candlesOfHours('200', '15', grid15)).toEqual({
      candles: 672,
      note: 'введено 200 ч — округлено до 672 свечи',
    });
  });

  it('takes no text that is no positive number', () => {
    expect(candlesOfHours('', '15', grid15)).toBeNull();
    expect(candlesOfHours('десять', '15', grid15)).toBeNull();
    expect(candlesOfHours('0', '15', grid15)).toBeNull();
  });
});

describe('durationText', () => {
  it.each([
    [3, '5', '15 мин'],
    [48, '15', '12 ч'],
    [6, '15', '1.5 ч'],
    [96, '60', '4 дн'],
    [32, '60', '1.3 дн'],
    [672, '15', '7 дн'],
  ])('writes %i candles of %s minutes as %s', (candles, interval, expected) => {
    expect(durationText(candles, interval)).toBe(expected);
  });
});

describe('overfitWarning', () => {
  const window = (current: string, candidate: string) => ({
    current: { expectancy: current },
    candidate: { expectancy: candidate },
  });

  // Scenario "Подгонка": +0.04 R on 90 days, +0.01 R on the last 30.
  it('warns when the last 30 days add less than half of what the 90 days add', () => {
    expect(overfitWarning(window('-0.06', '-0.02'), window('-0.09', '-0.08'))).toBe(
      'на последних 30 днях почти не лучше — похоже на подгонку',
    );
  });

  it('keeps quiet when the last 30 days add half of it or more, or the 90 days add nothing', () => {
    expect(overfitWarning(window('-0.06', '-0.02'), window('-0.09', '-0.07'))).toBeNull();
    expect(overfitWarning(window('-0.06', '-0.06'), window('-0.09', '-0.12'))).toBeNull();
  });

  it('keeps quiet without the statistics of a window', () => {
    expect(
      overfitWarning(window('-0.06', '-0.02'), { current: null, candidate: { expectancy: '0' } }),
    ).toBeNull();
  });
});

describe('sameRule', () => {
  const rule = { life: 24, target: '2', window: 4 };

  it('compares the values of two rules, the target as a number', () => {
    expect(sameRule(rule, { life: 24, target: '2.0', window: 4 })).toBe(true);
    expect(sameRule(rule, { life: 48, target: '2', window: 4 })).toBe(false);
    expect(sameRule(rule, { life: 24, target: '2.5', window: 4 })).toBe(false);
    expect(sameRule(rule, { life: 24, target: '2', window: 8 })).toBe(false);
    expect(sameRule(rule, { life: 24, target: null, window: 4 })).toBe(false);
    expect(
      sameRule({ life: 24, target: null, window: null }, { life: 24, target: null, window: null }),
    ).toBe(true);
  });
});
