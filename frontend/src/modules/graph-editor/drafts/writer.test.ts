import type { StrategyDef } from '../model/types';
import { drafts } from './drafts';
import { createDraftWriter } from './writer';

const definition = (nodes: number): StrategyDef => ({
  formatVersion: 1,
  direction: 'long',
  nodes: Array.from({ length: nodes }, (_, index) => ({ id: `n${index}`, kind: 'candle' })),
});

describe('draft writer', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-11-01T14:32:00Z'));
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('stores a series of edits once, as the last edit', () => {
    const save = vi.spyOn(drafts, 'save');
    const writer = createDraftWriter('key');

    writer.write({ name: 'EMA', definition: definition(1) });
    vi.advanceTimersByTime(100);
    writer.write({ name: 'EMA', definition: definition(2) });
    writer.write({ name: 'EMA', definition: definition(3) });
    vi.advanceTimersByTime(299);
    expect(save).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1);

    expect(save).toHaveBeenCalledTimes(1);
    expect(drafts.read('key')).toEqual({
      savedAt: '2026-11-01T14:32:00.400Z',
      name: 'EMA',
      definition: definition(3),
    });
  });

  it('drops a pending edit when cancelled', () => {
    const writer = createDraftWriter('key');

    writer.write({ name: 'EMA', definition: definition(1) });
    writer.cancel();
    vi.advanceTimersByTime(1000);

    expect(drafts.read('key')).toBeUndefined();
  });
});
