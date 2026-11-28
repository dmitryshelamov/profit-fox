import { describe, expect, it } from 'vitest';

import { scoreText, scoreTone } from './scoring';

// Change decimal-label-weights, task 4.2 (design.md, decision 5): a score or a weight with its sign and without noughts
// past its last digit.
describe('the text of a score', () => {
  it('signs a number above nought and leaves out the noughts after the point', () => {
    expect([1.25, '1.25', 2, '2.00', 0.5, '0.10', 100].map(scoreText)).toEqual([
      '+1.25',
      '+1.25',
      '+2',
      '+2',
      '+0.5',
      '+0.1',
      '+100',
    ]);
  });

  it('keeps the minus of a number below nought and writes nought bare', () => {
    expect([-0.5, '-0.50', -1, 0, '0.00', -0].map(scoreText)).toEqual([
      '-0.5',
      '-0.5',
      '-1',
      '0',
      '0',
      '0',
    ]);
  });

  it('writes a sum of weights to the hundredths, without the error of a binary fraction', () => {
    expect(scoreText(0.1 + 0.2)).toBe('+0.3');
    expect(scoreText(1 + 0.5 - 0.25 - 0.14)).toBe('+1.11');
    expect(scoreTone(0.1 + 0.2 - 0.3)).toBe('text-muted');
  });
});
