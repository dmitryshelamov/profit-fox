/** The object kept under a key of the browser storage; none when there is nothing, it is not an object or the storage is blocked. */
export function readStoredObject(key: string): Record<string, unknown> | null {
  try {
    const stored: unknown = JSON.parse(localStorage.getItem(key) ?? 'null');

    return typeof stored === 'object' && stored !== null && !Array.isArray(stored)
      ? (stored as Record<string, unknown>)
      : null;
  } catch {
    return null;
  }
}

export function writeStored(key: string, value: unknown): void {
  try {
    localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // The choice stays on the screen; it only is not remembered.
  }
}

/** The value when it is one of the allowed ones, otherwise the fallback: a stored value may be stale. */
export function oneOf<T extends string>(value: unknown, allowed: readonly T[], fallback: T): T {
  return allowed.find((item) => item === value) ?? fallback;
}
