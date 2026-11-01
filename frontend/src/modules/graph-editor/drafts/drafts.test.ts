import type { StrategyDef } from '../model/types';
import { draftKey, drafts, newStrategyDrafts } from './drafts';

const definition: StrategyDef = { formatVersion: 1, direction: 'long', nodes: [] };

describe('drafts in the browser', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  it('keeps a draft under its key until it is removed', () => {
    const key = draftKey({ strategy: 's1' });

    drafts.save(key, { name: 'EMA', definition }, new Date('2026-11-01T14:32:00Z'));

    expect(key).toBe('profit-fox:draft:strategy:s1');
    expect(drafts.read(key)).toEqual({
      savedAt: '2026-11-01T14:32:00.000Z',
      name: 'EMA',
      definition,
    });
    drafts.remove(key);
    expect(drafts.read(key)).toBeUndefined();
  });

  it('keys drafts of new strategies and nodes apart', () => {
    expect(draftKey({ newStrategy: 'd1' })).toBe('profit-fox:draft:strategy-new:d1');
    expect(draftKey({ node: 'n1' })).toBe('profit-fox:draft:node:n1');
  });

  it('lists the drafts of new strategies', () => {
    drafts.save(
      draftKey({ newStrategy: 'd1' }),
      { name: 'Моя', definition },
      new Date('2026-11-01T10:00:00Z'),
    );
    drafts.save(
      draftKey({ strategy: 's1' }),
      { name: 'EMA', definition },
      new Date('2026-11-01T10:00:00Z'),
    );

    expect(newStrategyDrafts()).toEqual([
      { draftId: 'd1', name: 'Моя', savedAt: '2026-11-01T10:00:00.000Z' },
    ]);
  });

  it('works without drafts when the browser refuses storage', () => {
    vi.stubGlobal('localStorage', {
      getItem: () => {
        throw new DOMException('denied', 'SecurityError');
      },
      setItem: () => {
        throw new DOMException('denied', 'SecurityError');
      },
      removeItem: () => {
        throw new DOMException('denied', 'SecurityError');
      },
      key: () => {
        throw new DOMException('denied', 'SecurityError');
      },
      get length(): number {
        throw new DOMException('denied', 'SecurityError');
      },
    });
    const key = draftKey({ strategy: 's1' });

    expect(drafts.save(key, { name: 'EMA', definition }, new Date())).toBe(false);
    expect(drafts.read(key)).toBeUndefined();
    expect(() => drafts.remove(key)).not.toThrow();
    expect(newStrategyDrafts()).toEqual([]);
  });

  it('ignores a stored value that is not a draft', () => {
    localStorage.setItem(draftKey({ strategy: 's1' }), '{not json');

    expect(drafts.read(draftKey({ strategy: 's1' }))).toBeUndefined();
  });
});
