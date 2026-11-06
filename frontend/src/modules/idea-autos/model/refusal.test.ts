import type { Schemas } from '@/shared/api/client';

import { refusalDetailText } from './refusal';

type Detail = Schemas['RefusalDetailResponse'];

const refusal = (reasons: string[], detail: Detail | null) => ({ reasons, detail });
const holder = (overrides: Partial<NonNullable<Detail['holder']>>) => ({
  kind: 'other_auto',
  autoName: 'Импульс лонг',
  above: true,
  paper: false,
  accountName: 'Bybit · ideas',
  since: '2026-11-07T13:05:00Z',
  ...overrides,
});
const withHolder = (overrides: Partial<NonNullable<Detail['holder']>>) =>
  refusal(['pair_taken'], { actual: null, limit: null, holder: holder(overrides) });

describe('detail of a refusal', () => {
  it('names the numbers of a reward to the risk below the lowest', () => {
    expect(
      refusalDetailText(
        refusal(['risk_reward_too_low'], { actual: '1.08', limit: '1.2', holder: null }),
      ),
    ).toBe('1.08 < 1.2');
  });

  it('names the free money and the amount', () => {
    expect(
      refusalDetailText(
        refusal(['not_enough_money'], { actual: '212.40', limit: '300', holder: null }),
      ),
    ).toBe('свободно 212.40 из 300 USDT');
  });

  it('names the sum of the day and the limit of the daily stop', () => {
    expect(
      refusalDetailText(refusal(['daily_stop'], { actual: '-30.15', limit: '30', holder: null })),
    ).toBe('сумма суток -30.15 при пределе 30');
  });

  it('names how many are open and the limit', () => {
    for (const reason of ['too_many_open_trades', 'too_many_same_direction']) {
      expect(refusalDetailText(refusal([reason], { actual: '5', limit: '5', holder: null }))).toBe(
        'открыто 5 при пределе 5',
      );
    }
  });

  it('names the risk and the amount against their limits', () => {
    expect(
      refusalDetailText(
        refusal(['total_risk_exceeded'], { actual: '41.2', limit: '40', holder: null }),
      ),
    ).toBe('риск 41.2 при пределе 40');
    expect(
      refusalDetailText(
        refusal(['amount_above_max'], { actual: '600', limit: '500', holder: null }),
      ),
    ).toBe('сумма 600 при пределе 500');
  });

  it('says whose trade holds the pair', () => {
    expect(refusalDetailText(withHolder({}))).toBe(
      'на Bybit · ideas её взял «Импульс лонг» — он выше в списке',
    );
    expect(refusalDetailText(withHolder({ above: false }))).toBe(
      'на Bybit · ideas её уже взял «Импульс лонг», с 13:05',
    );
    expect(
      refusalDetailText(
        withHolder({ kind: 'same_auto', paper: true, since: '2026-11-07T12:50:00Z' }),
      ),
    ).toBe('учебная этого автомата, с 12:50');
    expect(refusalDetailText(withHolder({ kind: 'same_auto' }))).toBe(
      'у этого автомата, открыта в 13:05',
    );
    expect(refusalDetailText(withHolder({ kind: 'manual' }))).toBe(
      'ручная на Bybit · ideas, с 13:05',
    );
    expect(refusalDetailText(withHolder({ paper: true }))).toBe(
      'учебная «Импульс лонг» на Bybit · ideas, с 13:05',
    );
  });

  it('judges by the first reason', () => {
    expect(
      refusalDetailText(
        refusal(['not_enough_money', 'risk_reward_too_low'], {
          actual: '1',
          limit: '2',
          holder: null,
        }),
      ),
    ).toBe('свободно 1.00 из 2 USDT');
  });

  it('has nothing to add for a reason with no detail', () => {
    expect(refusalDetailText(refusal(['late'], null))).toBe('—');
    expect(refusalDetailText(refusal(['auto_off'], null))).toBe('—');
  });
});
