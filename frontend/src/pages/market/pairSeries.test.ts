import {
  type IdeaLine,
  lineLabels,
  minMoveOf,
  panelHeight,
  rightRoom,
  scaleAmount,
  scalePrice,
  scaleRate,
} from './pairSeries';

const hour = 3_600_000;
const thirteen = Date.UTC(2026, 10, 22, 13);

function lines(to = thirteen + 4 * hour): IdeaLine[] {
  return [
    {
      id: 'long-1',
      kind: 'entry',
      price: 236.1,
      title: 'вход 236.10',
      from: thirteen,
      to,
      term: 'срок 17:00',
    },
    { id: 'long-1', kind: 'stop', price: 231.38, title: 'стоп 231.38', from: thirteen, to },
    { id: 'long-1', kind: 'target', price: 245.54, title: 'цель 245.54', from: thirteen, to },
  ];
}

// Task 19.14: the chart of a pair against the mockup 09-pair.
describe('the chart of a pair as the mockup draws it', () => {
  it('gives the panels of the open interest and the funding about 90 pixels each', () => {
    expect(panelHeight()).toBeGreaterThanOrEqual(85);
    expect(panelHeight()).toBeLessThanOrEqual(95);
  });

  it('writes the numbers of the scales with a point, the funding in percent', () => {
    expect(scalePrice(70.04)).toBe('70.0400');
    expect(scalePrice(245)).toBe('245.00');
    expect(scalePrice(61662.5).replace(/\s/g, ' ')).toBe('61 662.5');
    expect(scaleRate(-0.0015217)).toBe('-0.1522 %');
    expect(scaleRate(0.0003)).toBe('0.0300 %');
    expect(scaleAmount(1.15e9)).toBe('1.15 млрд');
    expect(scaleAmount(11_765_669)).toBe('11.8 млн');
    expect([scalePrice(70.04), scaleRate(0.0003), scaleAmount(11_765_669)].join(' ')).not.toContain(
      ',',
    );
  });

  it('steps the prices of the scale by five significant digits', () => {
    expect(minMoveOf(236.1)).toBe(0.01);
    expect(minMoveOf(0.00001234)).toBe(1e-9);
    expect(minMoveOf(61662.5)).toBe(1);
    expect(minMoveOf(undefined)).toBe(0.01);
  });

  it('writes «вход», «стоп», «цель» at the ends of the lines and «срок» between the entry and the line above', () => {
    expect(lineLabels(lines())).toEqual([
      { id: 'long-1', kind: 'entry', time: thirteen + 4 * hour, price: 236.1, text: 'вход 236.10' },
      {
        id: 'long-1',
        kind: 'term',
        time: thirteen + 4 * hour,
        price: (236.1 + 245.54) / 2,
        text: 'срок 17:00',
      },
      { id: 'long-1', kind: 'stop', time: thirteen + 4 * hour, price: 231.38, text: 'стоп 231.38' },
      {
        id: 'long-1',
        kind: 'target',
        time: thirteen + 4 * hour,
        price: 245.54,
        text: 'цель 245.54',
      },
    ]);
    expect(lineLabels([lines()[0]!]).map((label) => label.text)).toEqual([
      'вход 236.10 · срок 17:00',
    ]);
  });

  it('leaves room to the right of the last candle up to the term and for the labels, no more than a third', () => {
    const near = rightRoom(lines(), thirteen, '1h', 150, 6);
    expect(near.lines.map((line) => line.to)).toEqual([
      thirteen + 4 * hour,
      thirteen + 4 * hour,
      thirteen + 4 * hour,
    ]);
    expect(near.offset).toBe(Math.ceil((11 * 7 + 12) / 6));

    const far = rightRoom(lines(thirteen + 200 * hour), thirteen, '1h', 150, 6);
    expect(far.lines.map((line) => line.to)).toEqual([
      thirteen + 50 * hour,
      thirteen + 50 * hour,
      thirteen + 50 * hour,
    ]);
    expect(far.offset).toBe(0);

    expect(rightRoom([], thirteen, '1h', 150, 6).offset).toBe(10);
  });
});
