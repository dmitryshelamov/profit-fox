import { groupDigits, roundDecimal, showDecimal } from '@/shared/format/format';

const units = [
  { size: 1e9, name: 'млрд' },
  { size: 1e6, name: 'млн' },
  { size: 1e3, name: 'тыс.' },
] as const;

/**
 * A big amount in short form with a dot for the decimal, as the table of the mockup: billions with two decimals
 * («1.82 млрд»), otherwise one decimal below a hundred («48.2 млн») and none above («386 млн»); under a thousand, a whole
 * number.
 */
export function compactNumber(value: string): string {
  const amount = Number(value);
  const unit = units.find(({ size }) => amount >= size);

  if (!unit) {
    return String(Math.round(amount));
  }

  const scaled = amount / unit.size;
  const digits = unit.size === 1e9 ? 2 : scaled < 100 ? 1 : 0;

  return `${scaled.toFixed(digits)} ${unit.name}`;
}

/** A fraction as percent, by moving the point of the decimal string two places: the digits never become a number. */
function percentOf(fraction: string): string {
  const [integer = '', digits = ''] = fraction.split('.');
  const padded = digits.padEnd(2, '0');

  return `${integer}${padded.slice(0, 2)}.${padded.slice(2) || '0'}`;
}

/** The funding rate in percent with a sign and four decimals: «+0.0412», «-0.0034». */
export function fundingText(rate: string): string {
  const negative = rate.startsWith('-');

  return changeText(`${negative ? '-' : ''}${percentOf(rate.replace(/^-/, ''))}`, 4);
}

/** A change with its sign and the given decimals, without the unit: «+1.92», «-0.8». */
export function changeText(value: string, digits: number): string {
  return showDecimal(value, digits, true);
}

/** The spread in percent with two decimals; a spread under a hundredth that is not zero is «<0.01». */
export function spreadText(value: string): string {
  const rounded = roundDecimal(value, 2);

  return Number(rounded) === 0 && Number(value) > 0 ? '<0.01' : rounded;
}

/** How many instruments, with the word in the form that goes with the count: «1 104 инструмента». */
export function instrumentsText(count: number): string {
  const rest = count % 100;
  const word =
    rest >= 11 && rest <= 14
      ? 'инструментов'
      : rest % 10 === 1
        ? 'инструмент'
        : rest % 10 >= 2 && rest % 10 <= 4
          ? 'инструмента'
          : 'инструментов';

  return `${groupDigits(String(count))} ${word}`;
}

/** The direction of a change, which the table colours: growth, fall or none. */
export function trend(value: string): 'up' | 'down' | 'flat' {
  const amount = Number(value);

  if (amount > 0) {
    return 'up';
  }

  return amount < 0 ? 'down' : 'flat';
}
