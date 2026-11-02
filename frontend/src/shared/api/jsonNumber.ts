const decimal = /^([+-]?)(\d+)(?:\.(\d+))?$/;

/** A decimal as typed: an optional sign, digits and an optional fraction after a dot. No exponent, no hex, no comma. */
export function isDecimalText(text: string): boolean {
  return decimal.test(text);
}

/** A JSON number with exactly the digits it was made from; a JavaScript `number` would round them. */
export class JsonNumber {
  constructor(readonly literal: string) {}
}

/** The number for a text that {@link isDecimalText} accepts, spelled the way JSON does: no plus, no leading zeros. */
export function jsonNumberOf(text: string): JsonNumber {
  const [, sign, whole, fraction] = decimal.exec(text)!;

  return new JsonNumber(
    `${sign === '-' ? '-' : ''}${whole!.replace(/^0+(?=\d)/, '')}${fraction === undefined ? '' : `.${fraction}`}`,
  );
}

/** `JSON.stringify` that writes a {@link JsonNumber} with its own digits. */
export function serializeJson(body: unknown): string {
  const literals: string[] = [];
  const nonce = Math.random().toString(36).slice(2);
  const marker = `number-${nonce}-`;
  const text = JSON.stringify(body, (_key, value: unknown) =>
    value instanceof JsonNumber ? `${marker}${literals.push(value.literal) - 1}` : value,
  );

  return text.replace(
    new RegExp(`"${marker}(\\d+)"`, 'g'),
    (_match, index: string) => literals[Number(index)]!,
  );
}
