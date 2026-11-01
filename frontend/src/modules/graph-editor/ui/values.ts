import type { NodeValue, PinType } from '../model/types';

/** The text of a value as an input shows it. */
export function formatValue(value: NodeValue | undefined): string {
  return value === undefined ? '' : String(value);
}

const readers: Record<PinType, (text: string) => NodeValue | undefined> = {
  number: (text) => {
    const number = Number(text.replace(',', '.'));

    return text !== '' && Number.isFinite(number) ? number : undefined;
  },
  boolean: (text) => ({ true: true, false: false })[text],
  // The duration format of the API: [days.]hh:mm:ss.
  duration: (text) => (/^(\d+\.)?\d{2}:\d{2}:\d{2}$/.test(text) ? text : undefined),
};

/** The value a typed text stands for, or `undefined` for empty or unreadable text. */
export function parseValue(text: string, type: PinType): NodeValue | undefined {
  return readers[type](text.trim());
}
