import type { Schemas } from '@/shared/api/client';

import {
  closedCountText,
  durationText,
  excludedText,
  ideaComparisonText,
  ideaLeftText,
  ideaOutcomeText,
  leverageText,
  meanText,
  moneyText,
  outcomeCountsText,
  promiseComparisonText,
  promiseHint,
  promiseText,
} from './journal';

type Promise = Schemas['IdeaPromiseResponse'];
type Totals = Schemas['IdeaJournalTotalsResponse'];

const promise = (overrides: Partial<Promise> = {}): Promise => ({
  badge: 'up',
  probability: '0.31',
  expectancy: '0.14',
  cases: 412,
  breakEven: '0.351',
  netRiskReward: '1.8435',
  ...overrides,
});

const totals = (overrides: Partial<Totals> = {}): Totals => ({
  targets: 1,
  stops: 1,
  manual: 1,
  expired: 0,
  liquidated: 0,
  adl: 0,
  cancelled: 0,
  failed: 0,
  result: '0.9',
  fees: '0.12',
  meanR: '0.3',
  meanRLow: '-1.3437',
  meanRHigh: '1.9437',
  meanSecondsToOutcome: 5400,
  promise: { count: 0, promised: null, got: null },
  ideas: { count: 0, ideas: null, trades: null },
  ...overrides,
});

describe('time in a trade', () => {
  it('shows hours and minutes, days and hours, or minutes alone', () => {
    expect(durationText(8100)).toBe('2 ч 15 мин');
    expect(durationText(7200)).toBe('2 ч');
    expect(durationText(2700)).toBe('45 мин');
    expect(durationText(98_100)).toBe('1 д 3 ч');
    expect(durationText(86_400)).toBe('1 д');
  });

  it('says less than a minute, and a dash for no time', () => {
    expect(durationText(30)).toBe('< 1 мин');
    expect(durationText(null)).toBe('—');
  });

  it('takes the number as the API gives it, a text too', () => {
    expect(durationText('8100')).toBe('2 ч 15 мин');
  });
});

describe('promise of a trade', () => {
  // Scenario "Сделка с обещанием и исходом идеи".
  it('shows the badge, the expectancy in R and the cases', () => {
    expect(promiseText(promise())).toBe('▲ +0.14 R · N 412');
    expect(promiseText(promise({ badge: 'down', expectancy: '-0.2' }))).toBe('▼ -0.20 R · N 412');
  });

  it('leaves the badge out while the bucket had too few cases', () => {
    expect(
      promiseText(promise({ badge: null, probability: null, expectancy: '0.05', cases: 12 })),
    ).toBe('+0.05 R · N 12');
  });

  // Scenario "Сделка до выкатки".
  it('shows a dash for a trade without a promise or without a bucket', () => {
    expect(promiseText(null)).toBe('—');
    expect(
      promiseText(promise({ badge: null, probability: null, expectancy: null, cases: null })),
    ).toBe('—');
  });

  it('gives the probability, the break-even and the reward to risk for the hint', () => {
    expect(promiseHint(promise())).toBe('Вероятность 31 % · безубыток: нужно ≥ 36 % · R:R 1.84');
    expect(promiseHint(promise({ probability: null, breakEven: null }))).toBe(
      'Вероятность —, не окупает комиссии · R:R 1.84',
    );
  });
});

describe('outcome of the idea of a trade', () => {
  it('shows the status of the idea and its result in R', () => {
    expect(
      ideaOutcomeText({ status: 'target', statusAt: '2026-11-05T12:00:00Z', result: '1.84' }),
    ).toBe('Цель +1.84 R');
    expect(
      ideaOutcomeText({ status: 'stop', statusAt: '2026-11-05T12:00:00Z', result: '-1.06' }),
    ).toBe('Стоп -1.06 R');
  });

  it('shows the status alone for an idea that never entered, and a dash for no outcome', () => {
    expect(
      ideaOutcomeText({ status: 'expired', statusAt: '2026-11-05T12:00:00Z', result: null }),
    ).toBe('Истекла');
    expect(ideaOutcomeText(null)).toBe('—');
  });
});

describe('time left of the idea of a trade', () => {
  const now = Date.parse('2026-11-08T12:00:00Z');
  const outcome = { status: 'expired', statusAt: '2026-11-08T11:00:00Z', result: '0.4' } as const;

  // Scenario "Идея ещё жива".
  it('counts down to the end of an active idea, a minute less a minute later', () => {
    const trade = { ideaEndsAt: '2026-11-08T13:40:00Z', ideaOutcome: null };

    expect(ideaLeftText(trade, now)).toBe('1 ч 40 мин');
    expect(ideaLeftText(trade, now + 60_000)).toBe('1 ч 39 мин');
  });

  // Scenario "Срок идеи вышел, а сканер её ещё не закрыл".
  it('says less than a minute once the end has passed and the idea has no outcome yet', () => {
    expect(ideaLeftText({ ideaEndsAt: '2026-11-08T11:58:00Z', ideaOutcome: null }, now)).toBe(
      '< 1 мин',
    );
  });

  // Scenario "Идея закрылась раньше сделки".
  it('says closed when the trade has the outcome of its idea', () => {
    expect(ideaLeftText({ ideaEndsAt: null, ideaOutcome: outcome }, now)).toBe('закрыта');
  });

  // Scenario "Идея ждёт входа".
  it('shows a dash with neither an end nor an outcome of the idea', () => {
    expect(ideaLeftText({ ideaEndsAt: null, ideaOutcome: null }, now)).toBe('—');
  });
});

describe('leverage of a trade', () => {
  // Scenario "Плечо и залог сделки".
  it('shows the leverage and the margin of a trade on linear, the margin in USDT to cents', () => {
    expect(leverageText({ category: 'linear', leverage: 5, margin: '15.83' })).toBe(
      '5× · 15.83 USDT',
    );
  });

  // Scenario "Старая сделка с неизвестным плечом": not 1×, the leverage is unknown.
  it('shows a dash for a trade whose leverage is unknown', () => {
    expect(leverageText({ category: 'linear', leverage: null, margin: null })).toBe('—');
  });

  it('shows a dash off linear, where there is no leverage', () => {
    expect(leverageText({ category: 'spot', leverage: 1, margin: '79.15' })).toBe('—');
  });
});

describe('totals of the journal', () => {
  // Scenario "Итоги".
  it('shows the counts of the outcomes with their shares of the four', () => {
    expect(outcomeCountsText(totals({ expired: 1 }))).toBe(
      'по цели 1 (25 %) · по стопу 1 (25 %) · вручную 1 (25 %) · по сроку 1 (25 %)',
    );
    expect(outcomeCountsText(totals({ targets: 3, stops: 1, manual: 0 }))).toBe(
      'по цели 3 (75 %) · по стопу 1 (25 %) · вручную 0 (0 %) · по сроку 0 (0 %)',
    );
  });

  // Scenario "Есть ликвидация": the liquidated and the closed by ADL stand in the row only when there are any.
  it('adds the liquidated and the closed by ADL to the outcomes only when there are any', () => {
    expect(outcomeCountsText(totals({ targets: 1, stops: 2, manual: 0, liquidated: 1 }))).toBe(
      'по цели 1 (25 %) · по стопу 2 (50 %) · вручную 0 (0 %) · по сроку 0 (0 %) · ликвидировано 1 (25 %)',
    );
    expect(outcomeCountsText(totals({ targets: 1, stops: 0, manual: 0, adl: 1 }))).toBe(
      'по цели 1 (50 %) · по стопу 0 (0 %) · вручную 0 (0 %) · по сроку 0 (0 %) · ADL 1 (50 %)',
    );
  });

  it('shows a dash when nothing is closed', () => {
    expect(outcomeCountsText(totals({ targets: 0, stops: 0, manual: 0 }))).toBe('—');
  });

  it('counts the trades closed at expiry alone', () => {
    expect(outcomeCountsText(totals({ targets: 0, stops: 0, manual: 0, expired: 2 }))).toBe(
      'по цели 0 (0 %) · по стопу 0 (0 %) · вручную 0 (0 %) · по сроку 2 (100 %)',
    );
  });

  it('names the cancelled and the failed apart, and nothing when there are none', () => {
    expect(excludedText(totals({ cancelled: 1, failed: 2 }))).toBe('отменено 1 · с ошибкой 2');
    expect(excludedText(totals({ cancelled: 1 }))).toBe('отменено 1');
    expect(excludedText(totals())).toBeNull();
  });

  it('shows the mean in R with its interval, and the mean alone for one trade', () => {
    expect(meanText(totals())).toBe('+0.30 R (95 %: -1.34 … +1.94)');
    expect(meanText(totals({ meanR: '1.8', meanRLow: null, meanRHigh: null }))).toBe('+1.80 R');
    expect(meanText(totals({ meanR: null, meanRLow: null, meanRHigh: null }))).toBe('—');
  });

  it('shows the money and the fees', () => {
    expect(moneyText(totals())).toBe('+0.90 USDT · комиссии 0.12 USDT');
    expect(moneyText(totals({ result: '-1.2' }))).toBe('-1.20 USDT · комиссии 0.12 USDT');
  });

  // Scenarios "Обещали больше, чем получили" and "Сравнивать не с чем".
  it('compares the promised expectancy with the result of the same trades', () => {
    expect(promiseComparisonText({ count: 3, promised: '0.15', got: '-0.1333' })).toBe(
      'обещали +0.15 R → получили -0.13 R · N 3',
    );
    expect(promiseComparisonText({ count: 0, promised: null, got: null })).toBe('—');
  });

  it('compares the result of the ideas with the result of their trades', () => {
    expect(ideaComparisonText({ count: 2, ideas: '0.45', trades: '0.35' })).toBe(
      'идеи +0.45 R → сделки +0.35 R · N 2',
    );
    expect(ideaComparisonText({ count: 0, ideas: null, trades: null })).toBe('—');
  });
});

describe('count of the closed trades', () => {
  it('counts the trades closed at the target, at the stop, manually and at expiry, with the word that goes with the count', () => {
    expect(closedCountText(totals({ targets: 1, stops: 0, manual: 0 }))).toBe('1 сделка');
    expect(closedCountText(totals({ targets: 2, stops: 1, manual: 0 }))).toBe('3 сделки');
    expect(closedCountText(totals({ targets: 3, stops: 2, manual: 1 }))).toBe('6 сделок');
    expect(closedCountText(totals({ targets: 0, stops: 0, manual: 0 }))).toBe('0 сделок');
    expect(closedCountText(totals({ targets: 1, stops: 0, manual: 0, expired: 1 }))).toBe(
      '2 сделки',
    );
    expect(closedCountText(totals({ targets: 1, stops: 2, manual: 0, liquidated: 1 }))).toBe(
      '4 сделки',
    );
    expect(closedCountText(totals({ targets: 0, stops: 0, manual: 0, adl: 1 }))).toBe('1 сделка');
  });
});
