import type { Schemas } from '@/shared/api/client';

import {
  baselineBadge,
  expectancyInUsdt,
  expectancyText,
  probabilityView,
  riskOutcome,
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

describe('riskOutcome', () => {
  // Scenario "Риск на идею".
  it('gives the profit and the loss of an idea for the risk', () => {
    expect(riskOutcome('2', '10')).toEqual({ profit: '+20.0', loss: '−10.0' });
  });

  it('multiplies the digits exactly', () => {
    expect(riskOutcome('1.55', '12.5')).toEqual({ profit: '+19.4', loss: '−12.5' });
  });

  it('groups big amounts by thousands', () => {
    expect(riskOutcome('2', '1500')).toEqual({ profit: '+3\u00a0000.0', loss: '−1\u00a0500.0' });
  });

  it.each(['', 'abc', '0', '-5'])('has no outcome for the risk "%s"', (risk) => {
    expect(riskOutcome('2', risk)).toBeNull();
  });
});

describe('expectancyInUsdt', () => {
  // Scenario "Деньги в карточке".
  it('is the expectancy in R times the risk', () => {
    expect(expectancyInUsdt(bucket({ expectancy: '-0.05' }), '10')).toBe('-0.5');
    expect(expectancyInUsdt(bucket({ expectancy: '0.38' }), '10')).toBe('+3.8');
  });

  it('has no value for a bucket without a case', () => {
    expect(expectancyInUsdt(null, '10')).toBeNull();
  });

  it.each(['', 'abc', '0', '-5'])('has no value for the risk "%s"', (risk) => {
    expect(expectancyInUsdt(bucket(), risk)).toBeNull();
  });
});
