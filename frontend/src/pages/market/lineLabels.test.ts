import { labelHeight, layoutLabels } from './lineLabels';

/** Seven pixels a letter, as the monospace font of the labels about is. */
const measure = (text: string) => text.length * 7;

// Task 20.2: the labels of the lines of open ideas inside the area of the chart, apart from each other.
describe('the labels of the lines of an open idea', () => {
  it('writes a label to the right of the end of its line', () => {
    expect(layoutLabels([{ text: 'вход 236.10', x: 300, y: 120 }], 800, measure)).toEqual([
      { x: 306, y: 120 },
    ]);
  });

  it('keeps a label inside the right edge of the area of the chart', () => {
    const [placed] = layoutLabels([{ text: 'цель 19.361', x: 780, y: 120 }], 800, measure);

    expect(placed!.x + measure('цель 19.361')).toBeLessThanOrEqual(800);
  });

  it('moves apart the labels of levels closer than a line of text', () => {
    const placed = layoutLabels(
      [
        { text: 'вход 236.10', x: 300, y: 120 },
        { text: 'срок 17:00', x: 300, y: 124 },
        { text: 'стоп 235.90', x: 300, y: 126 },
      ],
      800,
      measure,
    );

    const ys = placed.map((label) => label.y).sort((a, b) => a - b);
    expect(ys[1]! - ys[0]!).toBeGreaterThanOrEqual(labelHeight);
    expect(ys[2]! - ys[1]!).toBeGreaterThanOrEqual(labelHeight);
    expect(placed[0]).toEqual({ x: 306, y: 120 });
  });

  it('leaves in place the labels of close levels that do not meet across the chart', () => {
    const placed = layoutLabels(
      [
        { text: 'вход 236.10', x: 100, y: 120 },
        { text: 'вход 236.20', x: 500, y: 122 },
      ],
      800,
      measure,
    );

    expect(placed.map((label) => label.y)).toEqual([120, 122]);
  });
});
