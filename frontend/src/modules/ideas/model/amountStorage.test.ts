import { loadAmount, saveAmount } from './amountStorage';

afterEach(() => {
  vi.restoreAllMocks();
  localStorage.clear();
});

describe('amount per idea in the browser', () => {
  it('keeps the value between visits', () => {
    saveAmount('25');

    expect(loadAmount()).toBe('25');
  });

  it('is 10 before anything is saved', () => {
    expect(loadAmount()).toBe('10');
  });

  it('keeps an amount the owner cleared', () => {
    saveAmount('');

    expect(loadAmount()).toBe('');
  });

  it('does not carry over the risk of the earlier version, which meant another thing', () => {
    localStorage.setItem('profit-fox.ideas.risk', '30');

    expect(loadAmount()).toBe('10');
  });

  it('works without the storage', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    expect(loadAmount()).toBe('10');
    expect(() => saveAmount('10')).not.toThrow();
  });
});
