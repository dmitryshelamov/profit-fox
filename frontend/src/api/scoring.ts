import { api, type Schemas, unwrap } from './client';
import { type Written, written } from './signals';

/** A row of the labels of «Скоринг» with its weights (change add-idea-score, spec idea-score «Раздел «Скоринг»»). */
export type ScoringLabel = Schemas['ScoringLabelResponse'];

/** The key of the labels with their weights, one for «Скоринг» and the hints of the labels of ideas. */
export const scoringLabelsKey = ['scoring', 'labels'] as const;

/** The labels of signals with their weights, those that are on first, and the built-in «⚡ всплеск» last. */
export async function listScoringLabels(): Promise<ScoringLabel[]> {
  return unwrap(await api.GET('/api/scoring/labels'));
}

/**
 * Sets the weights of the label with the text: numbers from −100 to +100 with at most two digits after the point for a long
 * and for a short (change decimal-label-weights, design.md, decision 2). A weight the server refuses comes back as errors at
 * `longWeight` or `shortWeight`; a text without a label throws.
 */
export async function setLabelWeight(
  text: string,
  longWeight: number,
  shortWeight: number,
): Promise<Written<ScoringLabel>> {
  return written(
    await api.PUT('/api/scoring/weights', { body: { text, longWeight, shortWeight } }),
  );
}

// A score or a weight to the hundredths: a sum of weights counted here, such as 0.1 + 0.2, carries the error of a binary
// fraction past them.
function hundredths(value: number | string): number {
  const number = Math.round(Number(value) * 100) / 100;
  return number === 0 ? 0 : number;
}

/**
 * A score or a weight with its sign and without noughts after its last digit (change decimal-label-weights, design.md,
 * decision 5): `+1.25`, `-0.5`, `0`, `+2`.
 */
export function scoreText(value: number | string): string {
  const number = hundredths(value);
  return number > 0 ? `+${String(number)}` : String(number);
}

/** The tone of a score or a weight: green above nought, red below, grey at nought. */
export function scoreTone(value: number | string): string {
  const number = hundredths(value);
  return number > 0 ? 'text-ok' : number < 0 ? 'text-err' : 'text-muted';
}
