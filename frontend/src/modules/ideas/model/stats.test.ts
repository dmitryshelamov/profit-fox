import type { Schemas } from '@/shared/api/client';

import {
  breakEvenText,
  expectancyHint,
  expectancyText,
  ideaBadge,
  moneyOutcome,
  outcomeShares,
  ownNumbers,
  perHundredText,
  probabilityHint,
  probabilityView,
} from './stats';

type Bucket = Schemas['BucketStatisticsResponse'];
type Numbers = Schemas['IdeaNumbersResponse'];

const numbers = (overrides: Partial<Numbers> = {}): Numbers => ({
  probability: '0.45',
  probabilityLow: '0.4',
  probabilityHigh: '0.5',
  multiplier: '1',
  band: null,
  expectancy: '0.12',
  expectancyLow: '0.002',
  expectancyHigh: '0.238',
  badge: 'up',
  costs: '0.08',
  expectancyPerUsdt: '0.0024',
  ...overrides,
});

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
  ownProbability: '0.4',
  ownProbabilityLow: '0.35',
  ownProbabilityHigh: '0.45',
  ownExpectancy: '0.3',
  ownExpectancyLow: '0.18',
  ownExpectancyHigh: '0.42',
  parentWeight: '0.2',
  fewOwnCases: false,
  grossExpectancy: '0.2',
  grossExpectancyLow: '0.08',
  grossExpectancyHigh: '0.32',
  ...overrides,
});

describe('probabilityView', () => {
  // Requirement "Раздел «Идеи»": the probability of the idea with N of the cases of its bucket.
  it('shows the probability of the idea in percent with N of its bucket', () => {
    expect(probabilityView(numbers({ probability: '0.1485' }), bucket())).toEqual({
      enough: true,
      percent: '15',
      cases: 400,
    });
  });

  // Scenario "Мало данных".
  it('says there is too little data while the idea has no probability', () => {
    expect(
      probabilityView(
        numbers({ probability: null }),
        bucket({ cases: 12, enoughData: false, probability: null }),
      ),
    ).toEqual({ enough: false, percent: null, cases: 12 });
  });

  it('says too little data for an idea without a bucket', () => {
    expect(probabilityView(numbers({ probability: null }), null)).toEqual({
      enough: false,
      percent: null,
      cases: 0,
    });
  });

  it('keeps the digits of a probability that is not a whole percent', () => {
    expect(probabilityView(numbers({ probability: '0.4649' }), bucket()).percent).toBe('46');
    expect(probabilityView(numbers({ probability: '0.465' }), bucket()).percent).toBe('47');
  });
});

describe('ideaBadge', () => {
  // Scenario "Ожидание надёжно выше нуля": the badge of the idea, by the interval of its own expectancy.
  it.each([
    ['up', '▲'],
    ['flat', '≈'],
    ['down', '▼'],
  ])('draws the badge %s of the idea as %s', (badge, symbol) => {
    expect(ideaBadge(numbers({ badge }))).toBe(symbol);
  });

  it('has no badge for an idea without an interval of its expectancy', () => {
    expect(ideaBadge(numbers({ badge: null }))).toBeNull();
  });
});

describe('probabilityHint', () => {
  // Scenario "Множитель «Уровней» по наведению": the probability of the bucket and the multiplier of the band, then the shares.
  it('starts with the probability of the bucket times the multiplier of the band of «Уровни»', () => {
    expect(
      probabilityHint(
        numbers({ probability: '0.1485', multiplier: '0.675', band: 'from_3_to_6' }),
        bucket({ probability: '0.22' }),
      ),
    ).toBe('корзина 22 % × 0.68 по R:R 3–6 · Цель 45 % · Стоп 50 % · Истекла 5 %');
  });

  it.each([
    ['up_to_2', 'до 2'],
    ['from_2_to_3', '2–3'],
    ['from_6', 'от 6'],
  ])('names the band %s as «%s»', (band, label) => {
    expect(
      probabilityHint(numbers({ multiplier: '1.375', band }), bucket({ probability: '0.22' })),
    ).toBe(`корзина 22 % × 1.38 по R:R ${label} · Цель 45 % · Стоп 50 % · Истекла 5 %`);
  });

  it('tells the shares alone for another type or a bucket without a probability', () => {
    expect(probabilityHint(numbers(), bucket())).toBe('Цель 45 % · Стоп 50 % · Истекла 5 %');
    expect(
      probabilityHint(
        numbers({ probability: null, band: 'from_3_to_6' }),
        bucket({ probability: null, enoughData: false }),
      ),
    ).toBe('Цель 45 % · Стоп 50 % · Истекла 5 %');
  });

  it('tells nothing for an idea without a bucket', () => {
    expect(probabilityHint(numbers({ probability: null }), null)).toBeNull();
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

  // Scenario "Откат по наведению": a bucket of «Отката» also tells the share of its signals that entered and the expectancy per signal.
  it('tells the entries and the expectancy per signal of a bucket of signals', () => {
    expect(
      outcomeShares(bucket({ signals: 800, entryShare: '0.5', signalExpectancy: '-0.03' })),
    ).toBe('Цель 45 % · Стоп 50 % · Истекла 5 % · входов 50 % · на сигнал -0.03 R');
  });
});

describe('ownNumbers', () => {
  // Scenario "Собственные числа по наведению".
  it('tells the own numbers of the bucket and the weight of the parent', () => {
    expect(
      ownNumbers(
        bucket({ cases: 35, ownProbability: '0.4', ownExpectancy: '0.3', parentWeight: '0.8' }),
      ),
    ).toBe('в самой корзине: 35 случаев · 40 % · +0.30 R · по родителю 80 %');
  });

  // Scenario "Мало своих данных": no own share under 30 own cases.
  it('tells the own expectancy of a bucket of few own cases without its share', () => {
    expect(
      ownNumbers(
        bucket({
          cases: 12,
          ownProbability: null,
          ownExpectancy: '0.5',
          parentWeight: '0.92',
          fewOwnCases: true,
        }),
      ),
    ).toBe('в самой корзине: 12 случаев · +0.50 R · по родителю 92 %');
  });

  it('tells nothing of a bucket without the model or without a bucket', () => {
    expect(ownNumbers(bucket({ enoughData: false, parentWeight: null }))).toBeNull();
    expect(ownNumbers(null)).toBeNull();
  });
});

describe('expectancyText', () => {
  // Scenario "Две идеи одной корзины": the expectancy of each idea in R.
  it('shows the expectancy of the idea in R with its sign', () => {
    expect(expectancyText(numbers({ expectancy: '-0.72' }))).toBe('-0.72 R');
    expect(expectancyText(numbers({ expectancy: '-0.062' }))).toBe('-0.06 R');
    expect(expectancyText(numbers({ expectancy: '0.376' }))).toBe('+0.38 R');
  });

  // Scenario "Идея без ожидания".
  it('shows a dash for an idea without an expectancy', () => {
    expect(expectancyText(numbers({ expectancy: null }))).toBe('—');
  });
});

describe('perHundredText', () => {
  // Scenario "Две идеи одной корзины": the expectancy on 100 USDT of the amount.
  it('shows the expectancy of the idea on 100 USDT of the amount', () => {
    expect(perHundredText(numbers({ expectancyPerUsdt: '-0.00216' }))).toBe('на 100 USDT: -0.22');
    expect(perHundredText(numbers({ expectancyPerUsdt: '-0.0031' }))).toBe('на 100 USDT: -0.31');
  });

  // Scenario "Идея без ожидания".
  it('shows nothing for an idea without an expectancy', () => {
    expect(perHundredText(numbers({ expectancy: null, expectancyPerUsdt: null }))).toBeNull();
  });
});

describe('expectancyHint', () => {
  // Scenario "Из чего сложено ожидание": the expectancy of the bucket without costs less the costs, then the own numbers of the bucket.
  it('starts with the expectancy of the bucket without costs less the costs of the idea', () => {
    expect(
      expectancyHint(
        numbers({ expectancy: '-0.72', costs: '0.7' }),
        bucket({
          grossExpectancy: '-0.02',
          cases: 35,
          ownProbability: '0.4',
          ownExpectancy: '0.3',
          parentWeight: '0.8',
        }),
      ),
    ).toBe(
      'без издержек -0.02 R − издержки 0.70 R\nв самой корзине: 35 случаев · 40 % · +0.30 R · по родителю 80 %',
    );
  });

  it('tells the own numbers alone for an idea without an expectancy', () => {
    expect(
      expectancyHint(
        numbers({ expectancy: null }),
        bucket({ grossExpectancy: null, cases: 35, parentWeight: '0.8', ownExpectancy: '0.3' }),
      ),
    ).toBe('в самой корзине: 35 случаев · 40 % · +0.30 R · по родителю 80 %');
  });

  it('tells nothing for an idea without a bucket', () => {
    expect(expectancyHint(numbers({ expectancy: null }), null)).toBeNull();
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
