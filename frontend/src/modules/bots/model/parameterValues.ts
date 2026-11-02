import { isDecimalText, type JsonNumber, jsonNumberOf } from '@/shared/api/jsonNumber';

export interface DeclaredParameter {
  readonly name: string;
  readonly type: 'number' | 'boolean' | 'duration';
}

/**
 * The `parameterValues` of a bot from the text of its form fields. An empty field is left out, so the default of the
 * strategy applies. A number keeps the digits typed; text that is no number goes as it is: the server reports it on the field.
 */
export function parameterValuesOf(
  declared: readonly DeclaredParameter[],
  texts: Readonly<Record<string, string>>,
): Record<string, JsonNumber | string> {
  const values: Record<string, JsonNumber | string> = {};

  for (const { name, type } of declared) {
    const text = (texts[name] ?? '').trim();

    if (text === '') {
      continue;
    }

    values[name] = type === 'number' && isDecimalText(text) ? jsonNumberOf(text) : text;
  }

  return values;
}
