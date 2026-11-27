import type { GraphValue, PinType } from './types';

/** A written value as the field on a pin shows it. */
export function formatValue(value: GraphValue | undefined): string {
  return value === undefined ? '' : String(value);
}

/**
 * The value typed into the field of a pin, or undefined for an empty field. A number keeps the digits typed:
 * `0.0005` goes to the JSON as `0.0005`. Text that is not a value of the type is kept as is, so the server's check
 * names it at its pin.
 */
export function parseValue(text: string, type: PinType): GraphValue | undefined {
  const trimmed = text.trim();
  if (trimmed === '') {
    return undefined;
  }

  if (type === 'number') {
    const number = Number(trimmed.replace(',', '.'));
    return Number.isFinite(number) ? number : trimmed;
  }

  if (type === 'boolean') {
    return trimmed === 'true' ? true : trimmed === 'false' ? false : trimmed;
  }

  return trimmed;
}
