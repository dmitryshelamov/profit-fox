import { createChecker } from './checker';

describe('check after edits', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('checks once a series of edits pauses, with the last edit', async () => {
    const run = vi.fn(async (edit: string) => `checked ${edit}`);
    const results: string[] = [];
    const checker = createChecker(
      run,
      (result) => results.push(result),
      () => undefined,
    );

    checker.request('a');
    vi.advanceTimersByTime(399);
    checker.request('b');
    vi.advanceTimersByTime(399);
    expect(run).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(1);

    expect(run).toHaveBeenCalledTimes(1);
    expect(run.mock.calls[0]![0]).toBe('b');
    expect(results).toEqual(['checked b']);
  });

  it('cancels a check in flight and ignores its answer when a newer edit comes', async () => {
    let finishA: (result: string) => void = () => undefined;
    const signals: AbortSignal[] = [];
    const run = vi.fn((edit: string, signal: AbortSignal) => {
      signals.push(signal);

      return edit === 'a'
        ? new Promise<string>((resolve) => (finishA = resolve))
        : Promise.resolve('checked b');
    });
    const results: string[] = [];
    const checker = createChecker(
      run,
      (result) => results.push(result),
      () => undefined,
    );

    checker.request('a');
    await vi.advanceTimersByTimeAsync(400);
    checker.request('b');
    expect(signals[0]!.aborted).toBe(true);
    finishA('checked a');
    await vi.advanceTimersByTimeAsync(400);

    expect(results).toEqual(['checked b']);
  });

  it('reports a failed check of the last edit', async () => {
    const failures: unknown[] = [];
    const checker = createChecker(
      async () => {
        throw new Error('502');
      },
      () => undefined,
      (error) => failures.push(error),
    );

    checker.request('a');
    await vi.advanceTimersByTimeAsync(400);

    expect(failures).toEqual([new Error('502')]);
  });

  it('does not report a check cancelled by a newer edit', async () => {
    const failures: unknown[] = [];
    const checker = createChecker(
      (edit: string, signal: AbortSignal) =>
        new Promise<string>((resolve, reject) => {
          signal.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')));
          if (edit === 'b') {
            resolve('checked b');
          }
        }),
      () => undefined,
      (error) => failures.push(error),
    );

    checker.request('a');
    await vi.advanceTimersByTimeAsync(400);
    checker.request('b');
    await vi.advanceTimersByTimeAsync(400);

    expect(failures).toEqual([]);
  });
});
