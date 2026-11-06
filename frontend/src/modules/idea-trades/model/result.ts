import { roundDecimal, signed } from '@/shared/format/format';

/** The result of a closed trade in money and in R, each with its sign; a dash before it is closed. */
export function resultText(result: string | null, resultR: string | null): string {
  if (result === null || resultR === null) {
    return '—';
  }

  return `${signed(roundDecimal(result, 2))} USDT · ${signed(roundDecimal(resultR, 2))} R`;
}
