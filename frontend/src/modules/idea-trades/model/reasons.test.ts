import { reasonText } from './reasons';

describe('reasons an entry is refused', () => {
  it('says each reason in words', () => {
    expect(reasonText('price_beyond_target')).toBe('Цена уже за целью идеи');
    expect(reasonText('price_beyond_stop')).toBe('Цена уже за стопом идеи');
    expect(reasonText('risk_reward_too_low')).toBe(
      'Прибыль к убытку ниже настройки «Прибыль к убытку не меньше»',
    );
    expect(reasonText('amount_above_max')).toBe('Сумма больше, чем «Максимум на одну сделку»');
    expect(reasonText('total_risk_exceeded')).toBe(
      'Если сработают все стопы, убыток будет больше «Максимального убытка»',
    );
    expect(reasonText('opposite_side_open')).toBe(
      'На символе открыта противоположная сторона: запущенный бот или другая сделка по идее',
    );
  });

  it('has words for every code the API gives', () => {
    const codes = [
      'idea_not_open',
      'price_beyond_stop',
      'price_beyond_target',
      'risk_reward_too_low',
      'amount_above_max',
      'too_many_open_trades',
      'total_risk_exceeded',
      'opposite_side_open',
      'not_enough_money',
      'quantity_below_minimum',
      'amount_below_minimum',
      'no_key',
      'category_not_traded',
      'hedge_mode',
      'stop_entry_not_supported',
    ];

    expect(codes.filter((code) => reasonText(code) === code)).toEqual([]);
  });

  it('shows a code it does not know as it is', () => {
    expect(reasonText('something_new')).toBe('something_new');
  });
});
