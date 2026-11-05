import type { Schemas } from '@/shared/api/client';

import {
  baselineBadge,
  expectancyInUsdt,
  expectancyText,
  moneyOutcome,
  probabilityView,
} from './stats';

type Bucket = Schemas['BucketStatisticsResponse'];

const bucket = (overrides: Partial<Bucket> = {}): Bucket => ({
  cases: 120,
  expired: 4,
  probability: '0.46',
  probabilityLow: '0.41',
  probabilityHigh: '0.51',
  baseline: '0.33',
  expectancy: '0.38',
  enoughData: true,
  ...overrides,
});

describe('baselineBadge', () => {
  it('is up when the whole interval is above the baseline', () => {
    expect(baselineBadge('0.41', '0.51', '0.33')).toBe('▲');
  });

  it('is down when the whole interval is below the baseline', () => {
    expect(baselineBadge('0.20', '0.30', '0.33')).toBe('▼');
  });

  it('is about equal when the interval holds the baseline', () => {
    expect(baselineBadge('0.28', '0.38', '0.33')).toBe('≈');
  });

  it('is about equal when the interval only touches the baseline', () => {
    expect(baselineBadge('0.33', '0.40', '0.33')).toBe('≈');
    expect(baselineBadge('0.25', '0.33', '0.33')).toBe('≈');
  });
});

describe('probabilityView', () => {
  // Scenario "Идея выше базовой линии".
  it('shows the probability in percent with N and the badge against the baseline', () => {
    expect(probabilityView(bucket(), '0.33')).toEqual({
      enough: true,
      percent: '46',
      cases: 120,
      badge: '▲',
    });
  });

  // Scenario "Мало данных".
  it('says there is too little data below 30 cases and shows no badge', () => {
    expect(
      probabilityView(bucket({ cases: 12, enoughData: false, probability: null }), '0.33'),
    ).toEqual({ enough: false, percent: null, cases: 12, badge: null });
  });

  it('says «мало данных» for a bucket whose cases all expired', () => {
    expect(
      probabilityView(
        bucket({
          cases: 30,
          enoughData: true,
          probability: null,
          probabilityLow: null,
          probabilityHigh: null,
        }),
        '0.33',
      ),
    ).toEqual({ enough: false, percent: null, cases: 30, badge: null });
  });

  it('says too little data for a bucket without a case', () => {
    expect(probabilityView(null, '0.33')).toEqual({
      enough: false,
      percent: null,
      cases: 0,
      badge: null,
    });
  });

  it('keeps the digits of a probability that is not a whole percent', () => {
    expect(probabilityView(bucket({ probability: '0.4649' }), '0.33').percent).toBe('46');
    expect(probabilityView(bucket({ probability: '0.465' }), '0.33').percent).toBe('47');
  });
});

describe('expectancyText', () => {
  it('shows the expectancy in R with its sign', () => {
    expect(expectancyText(bucket({ expectancy: '0.376' }))).toBe('+0.38 R');
    expect(expectancyText(bucket({ expectancy: '-0.1' }))).toBe('-0.10 R');
  });

  it('shows a dash for a bucket without a case', () => {
    expect(expectancyText(null)).toBe('—');
  });
});

const shares = { profitPerUsdt: '0.038878', lossPerUsdt: '0.021089' };

describe('moneyOutcome', () => {
  // Scenario "Риск на идею".
  it('gives the profit and the loss of an idea for the amount, after the fees', () => {
    expect(moneyOutcome(shares, '10')).toEqual({ profit: '+0.39', loss: '−0.21' });
  });

  it('multiplies the digits exactly', () => {
    expect(moneyOutcome(shares, '12.5')).toEqual({ profit: '+0.49', loss: '−0.26' });
  });

  it('groups big amounts by thousands', () => {
    expect(moneyOutcome(shares, '1500000')).toEqual({
      profit: '+58\u00a0317.00',
      loss: '−31\u00a0633.50',
    });
  });

  // Scenario "Цели 2R дают разную прибыль": the stop 3 times as far gives 3 times the money.
  it('gives more for an idea with a farther stop', () => {
    const near = moneyOutcome({ profitPerUsdt: '0.02', lossPerUsdt: '0.01' }, '100');
    const far = moneyOutcome({ profitPerUsdt: '0.06', lossPerUsdt: '0.03' }, '100');

    expect([near, far]).toEqual([
      { profit: '+2.00', loss: '−1.00' },
      { profit: '+6.00', loss: '−3.00' },
    ]);
  });

  it.each(['', 'abc', '0', '-5'])('has no outcome for the amount "%s"', (amount) => {
    expect(moneyOutcome(shares, amount)).toBeNull();
  });
});

describe('expectancyInUsdt', () => {
  // Scenario "Деньги в карточке": +0.5 R × 10 USDT × 0.02.
  it('is the expectancy in R times the amount times the risk on 1 USDT', () => {
    expect(expectancyInUsdt(bucket({ expectancy: '0.5' }), '0.02', '10')).toBe('+0.10');
    expect(expectancyInUsdt(bucket({ expectancy: '-0.05' }), '0.02', '10')).toBe('-0.01');
  });

  it('has no value for a bucket without a case', () => {
    expect(expectancyInUsdt(null, '0.02', '10')).toBeNull();
  });

  it.each(['', 'abc', '0', '-5'])('has no value for the amount "%s"', (amount) => {
    expect(expectancyInUsdt(bucket(), '0.02', amount)).toBeNull();
  });
});
