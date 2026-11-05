import { groupDigits, roundDecimal } from '@/shared/format/format';

/** A price with as many decimals as the price step of its pair has; as it is while the pair has no step. */
export function priceByTick(price: string, tick: string | null): string {
  if (tick === null) {
    return groupDigits(price);
  }

  return groupDigits(roundDecimal(price, (tick.split('.')[1] ?? '').length));
}
