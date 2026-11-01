import type { AnyDef } from '../model/types';

/** An unsaved edit of a strategy or composite node; it may be invalid. */
export interface Draft {
  readonly savedAt: string;
  readonly name: string;
  readonly definition: AnyDef;
}

export type DraftOf = { strategy: string } | { newStrategy: string } | { node: string };

const prefix = 'profit-fox:draft:';
const newStrategyPrefix = `${prefix}strategy-new:`;

export function draftKey(of: DraftOf): string {
  if ('strategy' in of) {
    return `${prefix}strategy:${of.strategy}`;
  }

  return 'newStrategy' in of ? `${newStrategyPrefix}${of.newStrategy}` : `${prefix}node:${of.node}`;
}

/**
 * Drafts in `localStorage`. The browser may refuse storage (private mode, blocked site data): then every operation
 * does nothing and the editor works without drafts.
 */
export const drafts = {
  /** Returns whether the draft was stored. */
  save(key: string, draft: Omit<Draft, 'savedAt'>, now: Date): boolean {
    return attempt(() => {
      localStorage.setItem(key, JSON.stringify({ savedAt: now.toISOString(), ...draft }));

      return true;
    }, false);
  },

  read(key: string): Draft | undefined {
    return attempt(() => {
      const stored = localStorage.getItem(key);

      return stored === null ? undefined : (JSON.parse(stored) as Draft);
    }, undefined);
  },

  remove(key: string): void {
    attempt(() => localStorage.removeItem(key), undefined);
  },
};

export function newStrategyDrafts(): { draftId: string; name: string; savedAt: string }[] {
  return attempt(() => {
    const keys = Array.from(
      { length: localStorage.length },
      (_, index) => localStorage.key(index) ?? '',
    );

    return keys
      .filter((key) => key.startsWith(newStrategyPrefix))
      .flatMap((key) => {
        const draft = drafts.read(key);

        return draft
          ? [
              {
                draftId: key.slice(newStrategyPrefix.length),
                name: draft.name,
                savedAt: draft.savedAt,
              },
            ]
          : [];
      });
  }, []);
}

function attempt<T>(operation: () => T, fallback: T): T {
  try {
    return operation();
  } catch {
    return fallback;
  }
}
