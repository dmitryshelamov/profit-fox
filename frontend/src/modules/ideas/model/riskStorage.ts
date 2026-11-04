const key = 'profit-fox.ideas.risk';

/** The risk per idea the owner typed last time, or an empty text; a browser without storage gives nothing. */
export function loadRisk(): string {
  try {
    return localStorage.getItem(key) ?? '';
  } catch {
    return '';
  }
}

export function saveRisk(risk: string): void {
  try {
    localStorage.setItem(key, risk);
  } catch {
    // The value stays on the screen; it only is not remembered.
  }
}
