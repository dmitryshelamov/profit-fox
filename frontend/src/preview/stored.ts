/** A small setting of the page kept in the browser: a remembered pair, a panel height. Never anything that must last. */
export function readStored<T>(key: string, fallback: T): T {
  try {
    const text = localStorage.getItem(key);
    return text === null ? fallback : (JSON.parse(text) as T);
  } catch {
    return fallback;
  }
}

export function writeStored(key: string, value: unknown): void {
  try {
    localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // A browser without storage remembers nothing; the page still works.
  }
}
