import { intervalLabel, intervalsOf } from '@/modules/market-data';

/** The intervals the chart offers besides the bot's own, where the category of the bot has them. */
const fixed = ['1', '5', '15', '60', '240', 'D'];

export interface ChartInterval {
  readonly value: string;
  readonly label: string;
  /** The interval of the bot: the only one with the current candle. */
  readonly own: boolean;
}

/**
 * The switcher: the fixed intervals the category of the bot has and the bot's own, each once, shortest first; the bot's is
 * marked «· бот».
 */
export function chartIntervals(own: string, category: string): ChartInterval[] {
  return intervalsOf(category)
    .filter((interval) => interval.value === own || fixed.includes(interval.value))
    .map((interval) => ({
      value: interval.value,
      label: interval.value === own ? `${intervalLabel(own)} · бот` : interval.label,
      own: interval.value === own,
    }));
}

/** The interval the chart shows: the one in the address if the switcher offers it, otherwise the bot's. */
export function shownInterval(asked: string | undefined, own: string, category: string): string {
  return asked !== undefined && chartIntervals(own, category).some((item) => item.value === asked)
    ? asked
    : own;
}
