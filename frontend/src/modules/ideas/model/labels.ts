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

/** How many ideas, with the word in the form that goes with the count: «21 идея». */
export function ideasText(count: number): string {
  const rest = count % 100;
  const word =
    rest >= 11 && rest <= 14
      ? 'идей'
      : rest % 10 === 1
        ? 'идея'
        : rest % 10 >= 2 && rest % 10 <= 4
          ? 'идеи'
          : 'идей';

  return `${groupDigits(String(count))} ${word}`;
}
