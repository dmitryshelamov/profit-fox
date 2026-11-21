import type { Graph, Layout } from '../model/types';

/** An unsaved edit of a signal or a strategy in the browser (spec signal-constructor «Черновик в браузере»). */
export interface Draft {
  readonly savedAt: string;
  readonly description: string;
  readonly graph: Graph;
  readonly layout: Layout;
}

const prefix = 'profit-fox:draft:';

/** A saved document by its name, or a new one by the id of its draft. */
export type DraftOwner = { readonly name: string } | { readonly newId: string };

/** The key of the draft of a saved signal or of a new one that has no name yet. */
export function draftKey(signal: DraftOwner): string {
  return 'name' in signal
    ? `${prefix}signal:${signal.name}`
    : `${prefix}signal-new:${signal.newId}`;
}

/**
 * The key of the draft of a strategy: a prefix of its own, so a strategy and a signal of one name never share a draft
 * (spec strategy-constructor «Черновики с одним именем»).
 */
export function strategyDraftKey(strategy: DraftOwner): string {
  return 'name' in strategy
    ? `${prefix}strategy:${strategy.name}`
    : `${prefix}strategy-new:${strategy.newId}`;
}

/** The draft under a key, or null when there is none, it does not parse, or the browser keeps no storage. */
export function readDraft(key: string): Draft | null {
  try {
    const text = localStorage.getItem(key);
    if (text === null) {
      return null;
    }

    const draft = JSON.parse(text) as Partial<Draft>;
    return draft.graph && draft.layout ? (draft as Draft) : null;
  } catch {
    return null;
  }
}

export function writeDraft(key: string, draft: Omit<Draft, 'savedAt'>): void {
  try {
    localStorage.setItem(key, JSON.stringify({ ...draft, savedAt: new Date().toISOString() }));
  } catch {
    // A browser without storage keeps no drafts; the editor still works.
  }
}

export function removeDraft(key: string): void {
  try {
    localStorage.removeItem(key);
  } catch {
    // Nothing to remove.
  }
}

/** Moves a draft to the key of a renamed signal or strategy. */
export function moveDraft(from: string, to: string): void {
  const draft = readDraft(from);
  if (draft !== null) {
    writeDraft(to, draft);
  }

  removeDraft(from);
}
