import { ideasText, sideLabel } from './labels';

describe('ideasText', () => {
  it.each([
    [0, '0 идей'],
    [1, '1 идея'],
    [2, '2 идеи'],
    [5, '5 идей'],
    [11, '11 идей'],
    [21, '21 идея'],
    [1234, '1 234 идеи'],
  ])('writes %i as %s', (count, expected) => {
    expect(ideasText(count)).toBe(expected);
  });
});

describe('sideLabel', () => {
  it('names the sides in Russian', () => {
    expect(sideLabel('long')).toBe('Лонг');
    expect(sideLabel('short')).toBe('Шорт');
  });
});
