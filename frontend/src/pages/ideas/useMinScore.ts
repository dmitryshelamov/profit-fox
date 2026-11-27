import { useEffect, useState } from 'react';

/** Where the browser keeps «Балл от»: one key for «Открытые» and «Журнал» (change add-idea-score, design.md, decision 4). */
export const minScoreKey = 'profit-fox.min-score';

/** The scores «Балл от» offers: from −3 to +9. */
export const minScores = Array.from({ length: 13 }, (_, index) => index - 3);

/** The event that tells the other tables of this tab the score chosen; the event `storage` tells other tabs. */
const chosenEvent = 'profit-fox:min-score';

function scoreOf(value: unknown): number | undefined {
  if (typeof value !== 'string' && typeof value !== 'number') {
    return undefined;
  }

  const number = Number(value);
  return value !== '' && Number.isInteger(number) && minScores.includes(number)
    ? number
    : undefined;
}

function readScore(): number | undefined {
  try {
    return scoreOf(localStorage.getItem(minScoreKey));
  } catch {
    return undefined;
  }
}

function writeScore(score: number | undefined): void {
  try {
    if (score === undefined) {
      localStorage.removeItem(minScoreKey);
    } else {
      localStorage.setItem(minScoreKey, String(score));
    }
  } catch {
    // A browser without storage remembers nothing; the choice still holds on the page.
  }
}

/**
 * «Балл от» above the tables of «Идеи» (spec idea-score «Балл в разделе «Идеи»»): no least score (`undefined`) or a whole
 * number from −3 to +9. One value for both tabs, kept in the browser; a browser that keeps nothing starts with none.
 */
export function useMinScore() {
  const [minScore, setMinScore] = useState<number | undefined>(readScore);
  useEffect(() => {
    const fromTab = (event: Event) => setMinScore(scoreOf((event as CustomEvent<unknown>).detail));
    const fromStorage = (event: StorageEvent) => {
      if (event.key === minScoreKey || event.key === null) {
        setMinScore(scoreOf(event.newValue));
      }
    };
    window.addEventListener(chosenEvent, fromTab);
    window.addEventListener('storage', fromStorage);
    return () => {
      window.removeEventListener(chosenEvent, fromTab);
      window.removeEventListener('storage', fromStorage);
    };
  }, []);

  return {
    minScore,
    choose: (next: number | undefined) => {
      setMinScore(next);
      writeScore(next);
      window.dispatchEvent(new CustomEvent(chosenEvent, { detail: next }));
    },
  };
}
