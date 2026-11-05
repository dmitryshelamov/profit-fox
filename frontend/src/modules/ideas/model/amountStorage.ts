const key = 'profit-fox.ideas.amount';
const defaultAmount = '10';

/** The amount per idea the owner typed last time, 10 before the first time; a browser without storage gives 10 too. */
export function loadAmount(): string {
  try {
    return localStorage.getItem(key) ?? defaultAmount;
  } catch {
    return defaultAmount;
  }
}

export function saveAmount(amount: string): void {
  try {
    localStorage.setItem(key, amount);
  } catch {
    // The value stays on the screen; it only is not remembered.
  }
}
