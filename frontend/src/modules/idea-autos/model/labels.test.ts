import {
  badgeHint,
  badgeLabel,
  conditionLines,
  modeCaption,
  modeLabel,
  reasonLabel,
} from './labels';

const auto = {
  setups: ['impulse', 'early_impulse'],
  types: ['pullback', 'target_2r'],
  intervals: ['15', '60'],
  side: 'long',
  category: 'linear',
  badge: 'up',
  minRiskReward: '1.5',
  minCases: 100,
};

describe('labels of autos', () => {
  it('names the modes and what each of them does', () => {
    expect(modeLabel('off')).toBe('Выкл');
    expect(modeLabel('paper')).toBe('Без денег');
    expect(modeLabel('live')).toBe('С деньгами');
    expect(modeCaption('live')).toBe('реальные ордера');
    expect(modeCaption('paper')).toBe('учебные сделки');
    expect(modeCaption('off')).toBe('не берёт идеи');
  });

  it('shows the badge as a symbol and says that a worse badge is not taken, a better one is', () => {
    expect(badgeLabel('up')).toBe('▲');
    expect(badgeLabel('flat')).toBe('≈');
    expect(badgeLabel('down')).toBe('▼');
    expect(badgeLabel(null)).toBe('любой значок');
    expect(badgeHint('up')).toBe('не хуже выбранного: только ▲');
    expect(badgeHint('flat')).toBe('не хуже выбранного: ▲ и ≈');
    expect(badgeHint('down')).toBe('не хуже выбранного: любой значок');
    expect(badgeHint(null)).toBe('не хуже выбранного: и идеи без значка');
  });

  it('writes the conditions in two lines: what is taken and where', () => {
    expect(conditionLines(auto)).toEqual({
      what: 'Импульс, Ранний импульс · Откат, Цель 2R',
      where: '15 мин, 1 ч · Лонг · Линейный · ▲ · R:R ≥ 1.5 · N ≥ 100',
    });
  });

  it('says both sides, both markets and any badge when nothing is chosen', () => {
    expect(
      conditionLines({ ...auto, side: null, category: null, badge: null, intervals: ['5'] }).where,
    ).toBe('5 мин · обе стороны · оба рынка · любой значок · R:R ≥ 1.5 · N ≥ 100');
  });

  it('names the reasons of a refusal and shows a code it does not know as it is', () => {
    expect(reasonLabel('pair_taken')).toBe('Уже есть сделка по паре');
    expect(reasonLabel('not_enough_money')).toBe('Не хватает денег');
    expect(reasonLabel('daily_stop')).toBe('Дневной стоп');
    expect(reasonLabel('too_many_same_direction')).toBe('Слишком много в одну сторону');
    expect(reasonLabel('late')).toBe('Не успел войти');
    expect(reasonLabel('auto_off')).toBe('Автомат выключен');
    expect(reasonLabel('risk_reward_too_low')).toBe('R:R ниже минимума');
    expect(reasonLabel('entry_error')).toBe('Ошибка подготовки входа');
    expect(reasonLabel('budget_taken')).toBe('Бюджет автомата занят');
    expect(reasonLabel('autos_budget_taken')).toBe('Бюджет автоматов занят');
    expect(reasonLabel('amount_above_max')).toBe('Больше максимума на одну сделку');
    expect(reasonLabel('total_risk_exceeded')).toBe('Максимальный убыток по стопам');
    expect(reasonLabel('stop_too_far_for_leverage')).toBe('Стоп дальше, чем позволяет плечо');
    expect(reasonLabel('leverage_differs')).toBe('На символе позиция с другим плечом');
    expect(reasonLabel('leverage_not_set')).toBe('Bybit не дал поставить плечо');
    expect(reasonLabel('something_new')).toBe('something_new');
  });
});
