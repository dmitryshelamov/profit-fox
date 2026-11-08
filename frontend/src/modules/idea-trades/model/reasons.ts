import type { Schemas } from '@/shared/api/client';
import { roundTenth } from '@/shared/format/format';

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
  stop_too_far_for_leverage: 'Стоп дальше, чем позволяет плечо',
  leverage_differs: 'На символе открыта позиция с другим плечом — плечо не поменять',
  leverage_not_set: 'Bybit не дал поставить плечо',
};

/** The numbers of a reason, as the API tells them. */
type Detail = Pick<Schemas['RefusalDetailResponse'], 'actual' | 'limit'>;

/** The reasons whose words carry their numbers. */
const told: Record<string, (detail: Detail) => string> = {
  stop_too_far_for_leverage: (detail) =>
    `Стоп дальше, чем позволяет плечо: ${roundTenth(detail.actual!)} % при пределе ${roundTenth(detail.limit!)} %`,
  leverage_differs: (detail) =>
    `На символе открыта позиция с плечом ${detail.actual}× — плечо не поменять`,
};

/** What stops an entry, in words, with its numbers when they are given; a code the interface does not know is shown as it is. */
export const reasonText = (code: string, detail?: Detail | null) =>
  (detail ? told[code]?.(detail) : undefined) ?? reasons[code] ?? code;
