import type { Graph, Layout } from '../model/types';

/** An unsaved edit of a signal in the browser (spec signal-constructor «Черновик в браузере»). */
export interface Draft {
  readonly savedAt: string;
  readonly description: string;
  readonly graph: Graph;
  readonly layout: Layout;
}

const prefix = 'profit-fox:draft:';

/** The key of the draft of a saved signal or of a new one that has no name yet. */
export function draftKey(signal: { readonly name: string } | { readonly newId: string }): string {
  return 'name' in signal
    ? `${prefix}signal:${signal.name}`
    : `${prefix}signal-new:${signal.newId}`;
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

/** Moves a draft to the key of a renamed signal. */
export function moveDraft(from: string, to: string): void {
  const draft = readDraft(from);
  if (draft !== null) {
    writeDraft(to, draft);
  }

  removeDraft(from);
}
