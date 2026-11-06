import { groupDigits } from '@/shared/format/format';

const types: Record<string, string> = {
  levels: 'Уровни',
  target_2r: 'Цель 2R',
  beyond_extreme: 'За экстремумом',
  pullback: 'Откат',
  stop_entry: 'Стоп-вход',
};

const statuses: Record<string, string> = {
  awaiting_entry: 'Ждёт входа',
  active: 'Активна',
  target: 'Цель',
  stop: 'Стоп',
  expired: 'Истекла',
};

export const ideaTypes = Object.entries(types).map(([value, label]) => ({ value, label }));

export const typeLabel = (code: string) => types[code] ?? code;
export const statusLabel = (code: string) => statuses[code] ?? code;
export const sideLabel = (code: string) => (code === 'long' ? 'Лонг' : 'Шорт');

/** The count with the word in the form that goes with it: one, a few (2–4), many. */
function counted(count: number, one: string, few: string, many: string): string {
  const rest = count % 100;
  const word =
    rest >= 11 && rest <= 14
      ? many
      : rest % 10 === 1
        ? one
        : rest % 10 >= 2 && rest % 10 <= 4
          ? few
          : many;

  return `${groupDigits(String(count))} ${word}`;
}

/** How many ideas, with the word in the form that goes with the count: «21 идея». */
export function ideasText(count: number): string {
  return counted(count, 'идея', 'идеи', 'идей');
}

/** How many cases of the replay: «603 кейса». */
export function casesText(count: number): string {
  return counted(count, 'кейс', 'кейса', 'кейсов');
}

/** How many own cases of a bucket: «35 случаев». */
export function bucketCasesText(count: number): string {
  return counted(count, 'случай', 'случая', 'случаев');
}

/** How many candles: «48 свечей». */
export function candlesText(count: number): string {
  return counted(count, 'свеча', 'свечи', 'свечей');
}
