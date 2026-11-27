import { act, renderHook } from '@testing-library/react';

import { instrumentKindKey, useInstrumentKind } from './useInstrumentKind';

// Change add-instrument-kind-filter, task 5.1: one choice of the kind of a pair for «Рынок», «Открытые» and «Журнал»,
// kept in the browser (design.md, decision 5).
describe('the kind of a pair chosen above the tables', () => {
  afterEach(() => vi.restoreAllMocks());

  it('is every kind when nothing is kept', () => {
    const { result } = renderHook(() => useInstrumentKind());

    expect(result.current.kind).toBeUndefined();
  });

  it('is kept in the browser and read by the next page', () => {
    const first = renderHook(() => useInstrumentKind());
    act(() => first.result.current.choose('stock'));

    expect(first.result.current.kind).toBe('stock');
    expect(localStorage.getItem(instrumentKindKey)).toBe('stock');
    first.unmount();
    expect(renderHook(() => useInstrumentKind()).result.current.kind).toBe('stock');
  });

  it('forgets the kind when every kind is chosen again', () => {
    localStorage.setItem(instrumentKindKey, 'crypto');
    const { result } = renderHook(() => useInstrumentKind());
    act(() => result.current.choose(undefined));

    expect(result.current.kind).toBeUndefined();
    expect(localStorage.getItem(instrumentKindKey)).toBeNull();
  });

  it('reads a kept value that is not a kind as every kind', () => {
    localStorage.setItem(instrumentKindKey, 'spot');

    expect(renderHook(() => useInstrumentKind()).result.current.kind).toBeUndefined();
  });

  // Scenario «Браузер не помнит».
  it('is every kind when the browser has no storage, and still chooses on the page', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('SecurityError');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError');
    });
    const { result } = renderHook(() => useInstrumentKind());

    expect(result.current.kind).toBeUndefined();
    act(() => result.current.choose('crypto'));
    expect(result.current.kind).toBe('crypto');
  });

  it('sees the kind chosen in another tab', () => {
    const { result } = renderHook(() => useInstrumentKind());

    act(() => {
      localStorage.setItem(instrumentKindKey, 'crypto');
      window.dispatchEvent(
        new StorageEvent('storage', { key: instrumentKindKey, newValue: 'crypto' }),
      );
    });
    expect(result.current.kind).toBe('crypto');

    act(() => {
      localStorage.removeItem(instrumentKindKey);
      window.dispatchEvent(new StorageEvent('storage', { key: instrumentKindKey, newValue: null }));
    });
    expect(result.current.kind).toBeUndefined();
  });

  it('is told to every table of the tab at once', () => {
    const market = renderHook(() => useInstrumentKind());
    const ideas = renderHook(() => useInstrumentKind());

    act(() => market.result.current.choose('stock'));

    expect(ideas.result.current.kind).toBe('stock');
  });
});
