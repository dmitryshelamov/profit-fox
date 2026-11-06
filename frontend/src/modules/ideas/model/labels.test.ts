import { bucketCasesText, casesText, ideasText, sideLabel } from './labels';

describe('casesText', () => {
  it.each([
    [1, '1 кейс'],
    [3, '3 кейса'],
    [12, '12 кейсов'],
    [603, '603 кейса'],
    [2412, '2\u00a0412 кейсов'],
  ])('writes %i as %s', (count, expected) => {
    expect(casesText(count)).toBe(expected);
  });
});

describe('bucketCasesText', () => {
  it.each([
    [1, '1 случай'],
    [21, '21 случай'],
    [3, '3 случая'],
    [12, '12 случаев'],
    [35, '35 случаев'],
  ])('writes %i as %s', (count, expected) => {
    expect(bucketCasesText(count)).toBe(expected);
  });
});

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
