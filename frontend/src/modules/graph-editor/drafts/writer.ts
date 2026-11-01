import { type Draft, drafts } from './drafts';

const delay = 300;

/** Stores a draft after edits pause for a moment, so a drag of a node is one write, not hundreds. */
export function createDraftWriter(key: string) {
  let pending: ReturnType<typeof setTimeout> | undefined;

  return {
    write(draft: Omit<Draft, 'savedAt'>) {
      clearTimeout(pending);
      pending = setTimeout(() => drafts.save(key, draft, new Date()), delay);
    },
    cancel() {
      clearTimeout(pending);
    },
  };
}
