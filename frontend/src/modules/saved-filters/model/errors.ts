const named: Record<string, string> = {
  name: 'Укажите имя фильтра.',
  place: 'Выберите, где показывать фильтр.',
  conditions: 'Добавьте хотя бы одно условие.',
};

const conditionParts: Record<string, string> = {
  field: 'выберите поле.',
  operator: 'выберите оператор, который подходит полю.',
  value: 'значение должно быть числом.',
  to: 'верхняя граница должна быть числом.',
};

/** The validation errors of the API, keyed by `name`, `place`, `conditions` or `conditions[i].field`, as sentences. */
export function filterErrorTexts(errors: Readonly<Record<string, readonly string[]>>): string[] {
  return Object.entries(errors).flatMap(([key, messages]) => {
    const condition = /^conditions\[(\d+)\]\.(\w+)$/.exec(key);
    const text =
      named[key] ??
      (condition ? `Условие ${Number(condition[1]) + 1}: ${conditionParts[condition[2]!]}` : null);

    return text === null ? [...messages] : [text];
  });
}
