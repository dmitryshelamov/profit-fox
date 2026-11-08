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

  // Scenario "Стоп слишком далеко": the share of the stop and its limit in percent, to a tenth.
  it('says the stop too far for the leverage with its share and its limit', () => {
    expect(reasonText('stop_too_far_for_leverage', { actual: '12.00', limit: '10' })).toBe(
      'Стоп дальше, чем позволяет плечо: 12 % при пределе 10 %',
    );
    expect(
      reasonText('stop_too_far_for_leverage', {
        actual: '20.00',
        limit: '16.666666666666666666666666667',
      }),
    ).toBe('Стоп дальше, чем позволяет плечо: 20 % при пределе 16.7 %');
  });

  it('says the leverage of a symbol that is taken', () => {
    expect(reasonText('leverage_differs', { actual: '1', limit: '5' })).toBe(
      'На символе открыта позиция с плечом 1× — плечо не поменять',
    );
  });

  it('says a reason whose words have no numbers without them', () => {
    expect(reasonText('risk_reward_too_low', { actual: '1.2', limit: '1.5' })).toBe(
      'Прибыль к убытку ниже настройки «Прибыль к убытку не меньше»',
    );
  });

  it('says the exchange did not set the leverage', () => {
    expect(reasonText('leverage_not_set')).toBe('Bybit не дал поставить плечо');
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
      'stop_too_far_for_leverage',
      'leverage_differs',
      'leverage_not_set',
    ];

    expect(codes.filter((code) => reasonText(code) === code)).toEqual([]);
  });

  it('shows a code it does not know as it is', () => {
    expect(reasonText('something_new')).toBe('something_new');
  });
});
