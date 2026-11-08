import type { Schemas } from '@/shared/api/client';

import {
  autosBudgetStateText,
  dayShare,
  dayStateText,
  openStateText,
  resumeText,
  riskStateText,
  sidesStateText,
} from './guards';

const guards = (overrides: Partial<Schemas['IdeaGuardsResponse']> = {}) => ({
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
  ...overrides,
});

describe('state of the guards under the settings', () => {
  it('says how many trades are open and how much risk stands', () => {
    expect(openStateText(guards())).toBe('сейчас открыто 3 из 8');
    expect(riskStateText(guards())).toBe('сейчас 10.22 из 40 USDT');
  });

  // Scenario "Состояние дневного стопа".
  it('says what the day has come to against the daily stop', () => {
    expect(dayStateText(guards())).toBe('Сегодня: -19.05 из 30 USDT');
    expect(dayStateText(guards({ dailyLossLimit: null }))).toBe(
      'Сегодня: -19.05 USDT · защита выключена',
    );
  });

  it('fills the bar by the share of the limit that the loss of the day has used', () => {
    expect(dayShare(guards())).toBeCloseTo(0.635);
    expect(dayShare(guards({ daySum: '-45' }))).toBe(1);
    expect(dayShare(guards({ daySum: '5' }))).toBe(0);
    expect(dayShare(guards({ dailyLossLimit: null }))).toBeNull();
  });

  // Scenarios "Состояние бюджета автоматов" and "Бюджет автоматов выключен".
  it('says what the real trades of the autos take of their budget', () => {
    expect(autosBudgetStateText(guards())).toBe('сейчас 180.00 из 200 USDT');
    expect(autosBudgetStateText(guards({ autosBudget: null }))).toBe(
      'сейчас 180.00 USDT · защита выключена',
    );
  });

  it('says how many real trades are open on each side against the limit', () => {
    expect(sidesStateText(guards())).toBe('открытых реальных: в лонг 2 · в шорт 1 из 5');
    expect(sidesStateText(guards({ maxSameDirection: null }))).toBe(
      'открытых реальных: в лонг 2 · в шорт 1 · защита выключена',
    );
  });

  it('says when the autos enter again once the daily stop has held', () => {
    expect(resumeText(guards())).toBeNull();
    expect(
      resumeText(
        guards({ triggeredAt: '2026-11-07T15:12:00Z', resumesAt: '2026-11-08T00:00:00Z' }),
      ),
    ).toBe('автоматы снова входят с 08.11 03:00');
  });
});
