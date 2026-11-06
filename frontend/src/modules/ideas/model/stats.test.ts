import type { Schemas } from '@/shared/api/client';

import {
  breakEvenText,
  expectancyText,
  moneyOutcome,
  outcomeShares,
  probabilityView,
} from './stats';

type Bucket = Schemas['BucketStatisticsResponse'];

const bucket = (overrides: Partial<Bucket> = {}): Bucket => ({
  cases: 400,
  targets: 180,
  stops: 200,
  expired: 20,
  probability: '0.45',
  probabilityLow: '0.4019',
  probabilityHigh: '0.4990',
  expectancy: '0.12',
  expectancyLow: '0.002',
  expectancyHigh: '0.238',
  badge: 'up',
  enoughData: true,
  signals: null,
  entryShare: null,
  signalExpectancy: null,
  ...overrides,
});

describe('probabilityView', () => {
  // Scenario "Ожидание надёжно выше нуля": the badge comes from the bucket, by the interval of its expectancy.
  it('shows the probability in percent with N and the badge of the bucket', () => {
    expect(probabilityView(bucket())).toEqual({
      enough: true,
      percent: '45',
      cases: 400,
      badge: '▲',
    });
  });

  // Scenario "Идея выше базовой линии": the interval of the expectancy holds zero.
  it.each([
    ['flat', '≈'],
    ['down', '▼'],
  ])('draws the badge %s as %s', (badge, symbol) => {
    expect(probabilityView(bucket({ badge })).badge).toBe(symbol);
  });

  // Scenario "Мало данных".
  it('says there is too little data below 30 cases and shows no badge', () => {
    expect(
      probabilityView(bucket({ cases: 12, enoughData: false, probability: null, badge: null })),
    ).toEqual({ enough: false, percent: null, cases: 12, badge: null });
  });

  it('says too little data for a bucket without a case', () => {
    expect(probabilityView(null)).toEqual({ enough: false, percent: null, cases: 0, badge: null });
  });

  it('keeps the digits of a probability that is not a whole percent', () => {
    expect(probabilityView(bucket({ probability: '0.4649' })).percent).toBe('46');
    expect(probabilityView(bucket({ probability: '0.465' })).percent).toBe('47');
  });
});

describe('breakEvenText', () => {
  // Scenario "Безубыток под вероятностью": rounded up, so «нужно ≥ 36 %» is never an understatement.
  it('rounds the break-even up to a whole percent', () => {
    expect(breakEvenText('0.3517')).toBe('нужно ≥ 36 %');
  });

  it('keeps a whole percent as it is', () => {
    expect(breakEvenText('0.35')).toBe('нужно ≥ 35 %');
    expect(breakEvenText('0.350')).toBe('нужно ≥ 35 %');
  });

  // Scenario "Цель не окупает комиссии".
  it('says the target does not cover the fees without a break-even', () => {
    expect(breakEvenText(null)).toBe('не окупает комиссии');
  });
});

describe('outcomeShares', () => {
  // Scenario "Доли по наведению".
  it('tells the shares of targets, stops and expired cases of the bucket', () => {
    expect(outcomeShares(bucket())).toBe('Цель 45 % · Стоп 50 % · Истекла 5 %');
  });

  it('rounds each share to a whole percent', () => {
    expect(outcomeShares(bucket({ cases: 3, targets: 1, stops: 2, expired: 0 }))).toBe(
      'Цель 33 % · Стоп 67 % · Истекла 0 %',
    );
  });

  it('tells nothing for a bucket without a case', () => {
    expect(outcomeShares(null)).toBeNull();
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
