import { useEffect, useState } from 'react';

/** Where the browser keeps «Балл от»: one key for «Открытые» and «Журнал» (change add-idea-score, design.md, decision 4). */
export const minScoreKey = 'profit-fox.min-score';

/**
 * The least score of the text typed in «Балл от» (change decimal-label-weights, design.md, decision 5): a number with at
 * most two digits after the point, as the server takes it, with a point or a comma; `undefined` for anything else. A whole number kept before the
 * scores had hundredths reads as the same number (risk 3).
 */
export function leastScoreOf(value: unknown): number | undefined {
  if (typeof value !== 'string' && typeof value !== 'number') {
    return undefined;
  }

  const text = decimalText(String(value));
  const number = Number(text);
  return /^-?\d+(\.\d{1,2})?$/.test(text) && Number.isFinite(number) ? number : undefined;
}

/**
 * A number as typed in a field of a score or a weight: spaces around dropped, a decimal comma read as a point and the
 * typographic minus as a hyphen, so «−0,5» reads as `-0.5` (a Russian keyboard types a comma).
 */
export function decimalText(text: string): string {
  return text.trim().replace(',', '.').replace('−', '-');
}

/** The event that tells the other tables of this tab the score chosen; the event `storage` tells other tabs. */
const chosenEvent = 'profit-fox:min-score';

const scoreOf = leastScoreOf;

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
 * «Балл от» above the tables of «Идеи» (spec idea-score «Балл в разделе «Идеи»»): no least score (`undefined`) or a
 * number with at most two digits after the point (change decimal-label-weights). One value for both tabs, kept in the
 * browser under the same key as before; a browser that keeps nothing starts with none.
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
