/** A snapshot older than this is stale: the scanner saves one every minute. */
const staleAfter = 180_000;

const ageOf = (snapshotAt: string, now: number) => Math.max(0, now - Date.parse(snapshotAt));

/** How long ago the snapshot was saved: «14 с назад», «7 мин назад», «3 ч назад». */
export function ageText(snapshotAt: string, now: number): string {
  const seconds = Math.floor(ageOf(snapshotAt, now) / 1000);

  if (seconds < 60) {
    return `${seconds} с назад`;
  }

  if (seconds < 3600) {
    return `${Math.floor(seconds / 60)} мин назад`;
  }

  return `${Math.floor(seconds / 3600)} ч назад`;
}

export function isStale(snapshotAt: string, now: number): boolean {
  return ageOf(snapshotAt, now) > staleAfter;
}
