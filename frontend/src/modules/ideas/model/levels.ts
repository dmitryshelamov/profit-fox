import type { LevelInput } from '@/modules/market-chart';

/** The three levels of an idea for the chart, each titled with its price. */
export function levelsOf(idea: {
  readonly entry: string;
  readonly stop: string;
  readonly target: string;
}): LevelInput[] {
  return [
    { kind: 'entry', price: idea.entry, title: `Вход ${idea.entry}` },
    { kind: 'stop', price: idea.stop, title: `Стоп ${idea.stop}` },
    { kind: 'target', price: idea.target, title: `Цель ${idea.target}` },
  ];
}
