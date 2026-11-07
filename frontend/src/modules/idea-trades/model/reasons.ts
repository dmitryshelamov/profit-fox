const reasons: Record<string, string> = {
  idea_not_open: 'Идея уже не ждёт входа и не активна',
  price_beyond_stop: 'Цена уже за стопом идеи',
  price_beyond_target: 'Цена уже за целью идеи',
  risk_reward_too_low: 'Прибыль к убытку ниже настройки «Прибыль к убытку не меньше»',
  amount_above_max: 'Сумма больше, чем «Максимум на одну сделку»',
  too_many_open_trades: 'Уже открыто столько сделок, сколько разрешено',
  total_risk_exceeded: 'Если сработают все стопы, убыток будет больше «Максимального убытка»',
  opposite_side_open:
    'На символе открыта противоположная сторона: запущенный бот или другая сделка по идее',
  not_enough_money: 'Не хватает свободных денег на аккаунте',
  quantity_below_minimum: 'Объём меньше минимального ордера биржи',
  amount_below_minimum: 'Сумма меньше минимальной суммы ордера биржи',
  no_key: 'У аккаунта нет рабочего ключа: выберите аккаунт',
  category_not_traded: 'Ключ аккаунта не торгует этот рынок',
  hedge_mode: 'На аккаунте включён режим hedge: переключите его на one-way',
  stop_entry_not_supported:
    'Стоп-вход пока не берётся: он ждёт выхода цены из диапазона, а вход здесь — по рынку или лимиткой',
};

/** What stops an entry, in words; a code the interface does not know is shown as it is. */
export const reasonText = (code: string) => reasons[code] ?? code;
