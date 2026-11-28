/** The weights of one label, as «Скоринг» gives them. */
export interface LabelWeights {
  readonly text: string;
  readonly longWeight: number | string;
  readonly shortWeight: number | string;
}

/** A label of an idea with what it adds to the score of the idea's side. */
export interface WeightedLabel {
  readonly text: string;
  readonly weight: number;
}

/** What the hint of the labels shows: the sum, the labels with a weight in their order, the rest. */
export interface LabelHint {
  readonly score: number;
  readonly weighted: readonly WeightedLabel[];
  readonly unweighted: readonly string[];
}

/**
 * The labels of an idea by the weights now (change label-score-hint, spec idea-labels «Подсказка меток с весами»): the
 * weight of each label for the side of the idea, pluses first from the largest, then minuses, the labels without a weight
 * apart in their own order; a label the weights do not know weighs nothing.
 */
export function labelHint(
  labels: readonly string[],
  side: string,
  weights: readonly LabelWeights[],
): LabelHint {
  const byText = new Map(weights.map((row) => [row.text, row]));
  const weighted: WeightedLabel[] = [];
  const unweighted: string[] = [];
  for (const text of labels) {
    const row = byText.get(text);
    const weight = row ? Number(side === 'long' ? row.longWeight : row.shortWeight) : 0;
    if (weight === 0) {
      unweighted.push(text);
    } else {
      weighted.push({ text, weight });
    }
  }

  weighted.sort((a, b) => b.weight - a.weight);
  return { score: weighted.reduce((sum, label) => sum + label.weight, 0), weighted, unweighted };
}
