import type { StrategyKind } from '@/modules/graph-editor/document';

/** A kind of a strategy in the words of the bench: its name, its short name in the list and what it means. */
export interface KindWords {
  readonly kind: StrategyKind;
  readonly title: string;
  readonly short: string;
  readonly explanation: string;
}

/** The two kinds in the order the dialog of a new strategy offers them. */
export const kinds: readonly KindWords[] = [
  {
    kind: 'idea',
    title: 'категория идей',
    short: 'идеи',
    explanation:
      'Вход по сигналу, выход по сроку, стопу или цели. Прогон сравнивает её со случайным входом.',
  },
  {
    kind: 'bot',
    title: 'для бота',
    short: 'бот',
    explanation:
      'Любой граф: лесенки, лимитные и рыночные заявки, закрытие позиции. Прогон считает сумму без сравнения.',
  },
];

export function wordsOf(kind: StrategyKind): KindWords {
  return kinds.find((words) => words.kind === kind)!;
}
