const openStatuses = new Set(['awaiting_entry', 'active']);

/** The ideas of a pair in two groups, each in the order it came: those still open, then the ones closed in 24 hours. */
export function groupPairIdeas<T extends { status: string }>(
  ideas: readonly T[],
): { open: T[]; closed: T[] } {
  return {
    open: ideas.filter((idea) => openStatuses.has(idea.status)),
    closed: ideas.filter((idea) => !openStatuses.has(idea.status)),
  };
}
