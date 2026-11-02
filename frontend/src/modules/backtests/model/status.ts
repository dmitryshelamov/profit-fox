import { groupDigits } from '../../../shared/format/format';

export interface Progress {
  readonly loadedCandles: number | string;
  readonly expectedCandles: number | string;
  readonly calculatedCombinations: number | string;
  readonly totalCombinations: number | string;
}

const titles: Record<string, string> = {
  Pending: 'В очереди',
  LoadingCandles: 'Загрузка свечей',
  Running: 'Считается',
  Succeeded: 'Готов',
  Failed: 'Ошибка',
  Cancelled: 'Отменён',
};

export function statusTitle(status: string): string {
  return titles[status] ?? status;
}

/** A finished backtest never changes, so nothing has to poll it. */
export function isFinished(status: string): boolean {
  return status === 'Succeeded' || status === 'Failed' || status === 'Cancelled';
}

/** Candles loaded of expected while loading, combinations calculated of all otherwise; none where it means nothing. */
export function progressText(status: string, progress: Progress): string | undefined {
  if (status === 'LoadingCandles') {
    return `${groupDigits(String(progress.loadedCandles))} / ${groupDigits(String(progress.expectedCandles))}`;
  }

  if (status === 'Running' || status === 'Cancelled') {
    return `${groupDigits(String(progress.calculatedCombinations))} / ${groupDigits(String(progress.totalCombinations))}`;
  }

  return undefined;
}

/** How far a loading or running backtest is, in whole percents; none for the other statuses. */
export function progressPercent(status: string, progress: Progress): number | undefined {
  const [done, all] =
    status === 'LoadingCandles'
      ? [progress.loadedCandles, progress.expectedCandles]
      : status === 'Running'
        ? [progress.calculatedCombinations, progress.totalCombinations]
        : [];

  if (done === undefined || all === undefined) {
    return undefined;
  }

  return BigInt(all) === 0n ? 0 : Number((BigInt(done) * 100n) / BigInt(all));
}
