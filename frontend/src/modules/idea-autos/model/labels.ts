import { counted, typeLabel } from '@/modules/ideas';
import { setupLabel } from '@/modules/market';
import { intervalLabel } from '@/modules/market-data';

export const modes = [
  { value: 'off', label: 'Выкл' },
  { value: 'paper', label: 'Без денег' },
  { value: 'live', label: 'С деньгами' },
] as const;

export const modeLabel = (mode: string) => modes.find((item) => item.value === mode)?.label ?? mode;

const captions: Record<string, string> = {
  live: 'реальные ордера',
  paper: 'учебные сделки',
  off: 'не берёт идеи',
};

/** What an auto in this mode does, under its name. */
export const modeCaption = (mode: string) => captions[mode] ?? mode;

export const badgeChoices = [
  { value: 'up', label: '▲' },
  { value: 'flat', label: '≈' },
  { value: 'down', label: '▼' },
  { value: '', label: 'любой' },
] as const;

const badgeSymbols: Record<string, string> = { up: '▲', flat: '≈', down: '▼' };

/** The lowest badge of an auto as a symbol; none takes ideas of any badge. */
export const badgeLabel = (badge: string | null) =>
  badge === null ? 'любой значок' : (badgeSymbols[badge] ?? badge);

const badgeHints: Record<string, string> = {
  up: 'только ▲',
  flat: '▲ и ≈',
  down: 'любой значок',
};

/** The condition of the badge lets through the badge chosen and the better ones. */
export const badgeHint = (badge: string | null) =>
  `не хуже выбранного: ${badge === null ? 'и идеи без значка' : (badgeHints[badge] ?? badge)}`;

export const sideChoices = [
  { value: 'long', label: 'Лонг' },
  { value: 'short', label: 'Шорт' },
  { value: '', label: 'Обе' },
] as const;

export const categoryChoices = [
  { value: 'linear', label: 'Линейный' },
  { value: 'spot', label: 'Спот' },
  { value: '', label: 'Оба' },
] as const;

interface Conditions {
  readonly setups: readonly string[];
  readonly types: readonly string[];
  readonly intervals: readonly string[];
  readonly side: string | null;
  readonly category: string | null;
  readonly badge: string | null;
  readonly minRiskReward: string;
  readonly minCases: number | string;
}

const sides: Record<string, string> = { long: 'Лонг', short: 'Шорт' };
const categories: Record<string, string> = { linear: 'Линейный', spot: 'Спот' };

/** The conditions of an auto in two lines: the setups and the types it takes, and the rest. */
export function conditionLines(auto: Conditions): { what: string; where: string } {
  return {
    what: `${auto.setups.map(setupLabel).join(', ')} · ${auto.types.map(typeLabel).join(', ')}`,
    where: [
      auto.intervals.map(intervalLabel).join(', '),
      auto.side === null ? 'обе стороны' : (sides[auto.side] ?? auto.side),
      auto.category === null ? 'оба рынка' : (categories[auto.category] ?? auto.category),
      badgeLabel(auto.badge),
      `R:R ≥ ${auto.minRiskReward}`,
      `N ≥ ${auto.minCases}`,
    ].join(' · '),
  };
}

const reasons: Record<string, string> = {
  idea_not_open: 'Идея уже не активна',
  price_beyond_stop: 'Цена уже за стопом',
  price_beyond_target: 'Цена уже за целью',
  risk_reward_too_low: 'R:R ниже минимума',
  amount_above_max: 'Сумма выше предела',
  too_many_open_trades: 'Не больше открытых сделок',
  total_risk_exceeded: 'Не больше общего риска',
  opposite_side_open: 'Открыта противоположная сторона',
  not_enough_money: 'Не хватает денег',
  quantity_below_minimum: 'Объём ниже минимума биржи',
  amount_below_minimum: 'Сумма ниже минимума биржи',
  no_key: 'Нет ключа аккаунта',
  category_not_traded: 'Ключ не торгует этот рынок',
  hedge_mode: 'Режим hedge на аккаунте',
  stop_entry_not_supported: 'Стоп-вход не берётся',
  daily_stop: 'Дневной стоп',
  too_many_same_direction: 'Слишком много в одну сторону',
  pair_taken: 'Уже есть сделка по паре',
  late: 'Не успел войти',
  auto_off: 'Автомат выключен',
  rules_unknown: 'Правила биржи неизвестны',
};

/** What stopped an auto, in words; a code the interface does not know is shown as it is. */
export const reasonLabel = (code: string) => reasons[code] ?? code;

/** «с деньгами» or «без денег»: the mode an auto had when it refused. */
export const refusalModeLabel = (mode: string) => (mode === 'live' ? 'с деньгами' : 'без денег');

/** How many trades, with the word in the form that goes with the count. */
export const dealsText = (count: number) => counted(count, 'сделка', 'сделки', 'сделок');
