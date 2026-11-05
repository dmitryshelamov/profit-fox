import { filterErrorTexts } from './errors';

describe('filterErrorTexts', () => {
  it('says in Russian what is wrong with the filter and which condition', () => {
    expect(
      filterErrorTexts({
        name: ['Name is required.'],
        place: ['x'],
        conditions: ['x'],
        'conditions[0].field': ['x'],
        'conditions[1].operator': ['x'],
        'conditions[1].value': ['x'],
        'conditions[2].to': ['x'],
      }),
    ).toEqual([
      'Укажите имя фильтра.',
      'Выберите, где показывать фильтр.',
      'Добавьте хотя бы одно условие.',
      'Условие 1: выберите поле.',
      'Условие 2: выберите оператор, который подходит полю.',
      'Условие 2: значение должно быть числом.',
      'Условие 3: верхняя граница должна быть числом.',
    ]);
  });

  it('shows an error it does not know as the server wrote it', () => {
    expect(filterErrorTexts({ other: ['Что-то'] })).toEqual(['Что-то']);
  });
});
