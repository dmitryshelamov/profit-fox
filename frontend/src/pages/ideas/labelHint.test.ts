import { describe, expect, it } from 'vitest';

import { labelHint } from './labelHint';

// Change label-score-hint, spec idea-labels «Подсказка меток с весами».
const weights = [
  { text: 'цена ↓ 4h', longWeight: 1, shortWeight: -1 },
  { text: '↓ BTC 1h', longWeight: -1, shortWeight: 1 },
  { text: 'OI ↓ 4h', longWeight: 1, shortWeight: 0 },
  { text: 'волат.↑ 15m', longWeight: 0, shortWeight: 0 },
];
const labels = ['OI ↓ 4h', 'волат.↑ 15m', 'цена ↓ 4h', '↓ BTC 1h'];

describe('the hint of the labels of an idea', () => {
  it('lists a long by its contributions, pluses first, and the labels without a weight apart', () => {
    expect(labelHint(labels, 'long', weights)).toEqual({
      score: 1,
      weighted: [
        { text: 'OI ↓ 4h', weight: 1 },
        { text: 'цена ↓ 4h', weight: 1 },
        { text: '↓ BTC 1h', weight: -1 },
      ],
      unweighted: ['волат.↑ 15m'],
    });
  });

  it('takes the short weights for a short', () => {
    expect(labelHint(labels, 'short', weights)).toEqual({
      score: 0,
      weighted: [
        { text: '↓ BTC 1h', weight: 1 },
        { text: 'цена ↓ 4h', weight: -1 },
      ],
      unweighted: ['OI ↓ 4h', 'волат.↑ 15m'],
    });
  });

  it('puts every label without a weight in one line, a label unknown to the weights too', () => {
    expect(labelHint(['волат.↑ 15m', 'нет такой'], 'long', weights)).toEqual({
      score: 0,
      weighted: [],
      unweighted: ['волат.↑ 15m', 'нет такой'],
    });
  });
});
