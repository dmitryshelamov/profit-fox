import { budgetText, guardParts, paperText, realText } from './totals';

const item = (overrides: object) => ({
  paperClosed: 61,
  paperMeanR: '0.0713',
  paperSumR: '4.27',
  realClosed: 0,
  realResult: '0',
  refusalsToday: 5,
  ...overrides,
});

describe('totals of an auto', () => {
  it('shows the paper trades with their mean and their sum in R', () => {
    expect(paperText(item({}))).toEqual({ main: '61 · ср. +0.07 R', sum: 'итог +4.27 R' });
    expect(paperText(item({ paperClosed: 24, paperMeanR: '-0.05', paperSumR: '-1.2' }))).toEqual({
      main: '24 · ср. -0.05 R',
      sum: 'итог -1.20 R',
    });
  });

  it('says there were none when no paper trade closed', () => {
    expect(paperText(item({ paperClosed: 0, paperMeanR: null, paperSumR: '0' }))).toEqual({
      main: '—',
      sum: 'не было',
    });
  });

  // Scenario "Бюджет в списке".
  it('shows what the open trades take of the budget, or that there is none', () => {
    expect(budgetText({ budgetUsed: '79.9', auto: { budget: '100' } })).toBe(
      'занято 79.90 из 100 USDT',
    );
    expect(budgetText({ budgetUsed: '0', auto: { budget: '50' } })).toBe('занято 0.00 из 50 USDT');
    expect(budgetText({ budgetUsed: '30', auto: { budget: null } })).toBe('без бюджета');
  });

  it('shows the real trades with their sum in USDT, or that there were none', () => {
    expect(realText(item({ realClosed: 11, realResult: '9.4' }))).toEqual({
      main: '11 сделок',
      sum: '+9.40 USDT',
    });
    expect(realText(item({ realClosed: 1, realResult: '-3.1' }))).toEqual({
      main: '1 сделка',
      sum: '-3.10 USDT',
    });
    expect(realText(item({}))).toEqual({ main: '—', sum: 'не было' });
  });
});

const guards = {
  openTrades: 3,
  maxOpenTrades: 8,
  openRisk: '10.2234',
  maxTotalRisk: '40',
  daySum: '-19.05',
  dailyLossLimit: '30',
  triggeredAt: null,
  resumesAt: null,
  openLongs: 2,
  openShorts: 1,
  maxSameDirection: 5,
  autosBudgetUsed: '180',
  autosBudget: '200',
};

describe('line of the guards', () => {
  it('shows the state of every guard', () => {
    expect(guardParts(guards).map((part) => `${part.name} ${part.value}`)).toEqual([
      'открыто 3 из 8',
      'риск 10.22 из 40 USDT',
      'сегодня -19.05 из 30 USDT',
      'в лонг 2 · в шорт 1 из 5',
      'бюджет автоматов 180.00 из 200 USDT',
    ]);
  });

  it('says that a guard that is off is off', () => {
    const parts = guardParts({
      ...guards,
      dailyLossLimit: null,
      maxSameDirection: null,
      autosBudget: null,
    });

    expect(parts[2]).toMatchObject({ name: 'сегодня', value: '-19.05 USDT · выкл' });
    expect(parts[3]).toMatchObject({ name: 'в лонг 2 · в шорт 1', value: 'выкл' });
    expect(parts[4]).toMatchObject({ name: 'бюджет автоматов', value: '180.00 USDT · выкл' });
  });
});
