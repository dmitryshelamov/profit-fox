import { loadRisk, saveRisk } from './riskStorage';

afterEach(() => {
  vi.restoreAllMocks();
  localStorage.clear();
});

describe('risk per idea in the browser', () => {
  it('keeps the value between visits', () => {
    saveRisk('25');

    expect(loadRisk()).toBe('25');
  });

  it('is 10 before anything is saved', () => {
    expect(loadRisk()).toBe('10');
  });

  it('keeps a risk the owner cleared', () => {
    saveRisk('');

    expect(loadRisk()).toBe('');
  });

  it('works without the storage', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    expect(loadRisk()).toBe('10');
    expect(() => saveRisk('10')).not.toThrow();
  });
});
