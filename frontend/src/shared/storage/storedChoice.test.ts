import { oneOf, readStoredObject, writeStored } from './storedChoice';

afterEach(() => {
  vi.restoreAllMocks();
  localStorage.clear();
});

describe('readStoredObject', () => {
  it('gives back what writeStored kept', () => {
    writeStored('k', { a: 1, b: 'x' });

    expect(readStoredObject('k')).toEqual({ a: 1, b: 'x' });
  });

  it.each([
    ['nothing stored', null],
    ['broken text', '{not json'],
    ['a number', '5'],
    ['null', 'null'],
    ['an array', '[1]'],
  ])('gives nothing for %s', (_, stored) => {
    if (stored !== null) {
      localStorage.setItem('k', stored);
    }

    expect(readStoredObject('k')).toBeNull();
  });

  it('works without the storage', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    expect(readStoredObject('k')).toBeNull();
    expect(() => writeStored('k', {})).not.toThrow();
  });
});

describe('oneOf', () => {
  it('keeps a value that is allowed and falls back otherwise', () => {
    expect(oneOf('b', ['a', 'b'], 'a')).toBe('b');
    expect(oneOf('c', ['a', 'b'], 'a')).toBe('a');
    expect(oneOf(5, ['a', 'b'], 'a')).toBe('a');
  });
});
