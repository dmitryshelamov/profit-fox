import { act, renderHook } from '@testing-library/react';

import { useDebounced } from './useDebounced';

describe('useDebounced', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('gives the first value at once and a changed one after the pause', () => {
    const { result, rerender } = renderHook(({ value }) => useDebounced(value, 300), {
      initialProps: { value: 'a' },
    });

    expect(result.current).toBe('a');

    rerender({ value: 'b' });
    act(() => void vi.advanceTimersByTime(299));
    expect(result.current).toBe('a');

    act(() => void vi.advanceTimersByTime(1));
    expect(result.current).toBe('b');
  });

  it('starts the pause again with each change, so only the last value comes', () => {
    const { result, rerender } = renderHook(({ value }) => useDebounced(value, 300), {
      initialProps: { value: 'a' },
    });

    rerender({ value: 'b' });
    act(() => void vi.advanceTimersByTime(200));
    rerender({ value: 'c' });
    act(() => void vi.advanceTimersByTime(200));

    expect(result.current).toBe('a');

    act(() => void vi.advanceTimersByTime(100));

    expect(result.current).toBe('c');
  });
});
