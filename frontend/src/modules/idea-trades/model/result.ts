import { roundDecimal, signed } from '@/shared/format/format';

/** The result of a closed trade in money and in R, each with its sign; a dash before it is closed. */
export function resultText(result: string | null, resultR: string | null): string {
  if (result === null || resultR === null) {
    return '—';
  }

  return `${signed(result)} USDT · ${signed(roundDecimal(resultR, 2))} R`;
}
