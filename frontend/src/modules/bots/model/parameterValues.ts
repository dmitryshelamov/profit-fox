export interface DeclaredParameter {
  readonly name: string;
  readonly type: 'number' | 'boolean' | 'duration';
}

/**
 * The `parameterValues` of a bot from the text of its form fields. An empty field is left out, so the default of the
 * strategy applies. Text that is no number goes as it is: the server reports it on the field.
 */
export function parameterValuesOf(
  declared: readonly DeclaredParameter[],
  texts: Readonly<Record<string, string>>,
): Record<string, number | string> {
  const values: Record<string, number | string> = {};

  for (const { name, type } of declared) {
    const text = (texts[name] ?? '').trim();

    if (text === '') {
      continue;
    }

    values[name] = type === 'number' && Number.isFinite(Number(text)) ? Number(text) : text;
  }

  return values;
}
