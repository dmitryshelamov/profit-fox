import { changeText, trend } from '../model/numbers';

const tones = { up: 'text-ok', down: 'text-err', flat: '' } as const;

/**
 * A change with its sign and the given decimals, green for growth and red for a fall; an empty value is a dash. The
 * unit follows only when asked: the table has it in the header.
 */
export function PercentChange({
  value,
  digits = 2,
  unit = '',
}: {
  value: string | null;
  digits?: number;
  unit?: string;
}) {
  return value === null ? (
    '—'
  ) : (
    <span className={tones[trend(value)]}>
      {changeText(value, digits)}
      {unit}
    </span>
  );
}
