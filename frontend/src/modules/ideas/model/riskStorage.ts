const key = 'profit-fox.ideas.risk';
const defaultRisk = '10';

/** The risk per idea the owner typed last time, 10 before the first time; a browser without storage gives 10 too. */
export function loadRisk(): string {
  try {
    return localStorage.getItem(key) ?? defaultRisk;
  } catch {
    return defaultRisk;
  }
}

export function saveRisk(risk: string): void {
  try {
    localStorage.setItem(key, risk);
  } catch {
    // The value stays on the screen; it only is not remembered.
  }
}
