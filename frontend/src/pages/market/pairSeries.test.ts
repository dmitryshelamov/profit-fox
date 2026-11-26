import { idea } from '@/test/ideas';

import {
  type IdeaLine,
  ideaLines,
  ideaMarks,
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
      {
        id: 'long-1',
        kind: 'entry',
        time: thirteen + 4 * hour,
        from: thirteen,
        price: 236.1,
        text: 'вход 236.10',
      },
      {
        id: 'long-1',
        kind: 'term',
        time: thirteen + 4 * hour,
        from: thirteen,
        price: (236.1 + 245.54) / 2,
        text: 'срок 17:00',
      },
      {
        id: 'long-1',
        kind: 'stop',
        time: thirteen + 4 * hour,
        from: thirteen,
        price: 231.38,
        text: 'стоп 231.38',
      },
      {
        id: 'long-1',
        kind: 'target',
        time: thirteen + 4 * hour,
        from: thirteen,
        price: 245.54,
        text: 'цель 245.54',
      },
    ]);
    expect(lineLabels([lines()[0]!]).map((label) => label.text)).toEqual([
      'вход 236.10 · срок 17:00',
    ]);
  });

  it('leaves room to the right of the last candle up to the term and for the labels, no more than a third', () => {
    const labels = Math.ceil((11 * 7 + 12) / 6);
    const near = rightRoom(lines(), thirteen, '1h', 150, 6);
    expect(near.lines.map((line) => line.to)).toEqual([
      thirteen + 4 * hour,
      thirteen + 4 * hour,
      thirteen + 4 * hour,
    ]);
    // Task 20.2: the slots up to the term are empty points of the time scale; the offset is past the term, for labels.
    expect(near.slots).toEqual([1, 2, 3, 4].map((bars) => thirteen + bars * hour));
    expect(near.offset).toBe(labels);

    const far = rightRoom(lines(thirteen + 200 * hour), thirteen, '1h', 150, 6);
    expect(far.lines.map((line) => line.to)).toEqual([
      thirteen + 50 * hour,
      thirteen + 50 * hour,
      thirteen + 50 * hour,
    ]);
    expect(far.slots).toHaveLength(50);
    expect(far.slots.at(-1)).toBe(thirteen + 50 * hour);
    // Task 20.2: a term past the third still leaves the labels room to the right of the end of the lines.
    expect(far.offset).toBe(labels);

    expect(rightRoom([], thirteen, '1h', 150, 6).offset).toBe(10);
    expect(rightRoom([], thirteen, '1h', 150, 6).slots).toEqual([]);
  });
});

// Spec market-overview «Идеи на графике» (change fix-pair-chart-and-local-time, task 2.1): without a chosen idea only the
// arrows of the entries, with one only it, whole.
describe('the ideas a chart of a pair draws', () => {
  const shortClosed = idea({
    id: 'short-1',
    strategy: 'funding-short',
    timeframe: '4h',
    side: 'short',
    state: 'closed',
    moment: Date.UTC(2026, 10, 21, 8),
    exitTime: Date.UTC(2026, 10, 21, 12),
    exitReason: 'time',
    result: 0.0041,
  });
  const longOpen = idea({ id: 'long-1', until: Date.UTC(2026, 10, 22, 17) });
  const forty = Array.from({ length: 40 }, (_, index) =>
    index < 3
      ? idea({ id: `open-${index}`, moment: thirteen - index * hour })
      : idea({
          id: `closed-${index}`,
          side: index % 2 === 0 ? 'short' : 'long',
          state: 'closed',
          moment: thirteen - index * hour,
          exitTime: thirteen - index * hour + 30 * 60_000,
          exitReason: 'stop',
          result: -0.02,
        }),
  );

  // Scenario «Без выбора».
  it('draws only an arrow of the entry of every idea, without text, exits and lines, when none is chosen', () => {
    const marks = ideaMarks(forty, '1h', null);
    expect(marks).toHaveLength(40);
    expect(marks.every((mark) => mark.kind === 'entry' && mark.text === '')).toBe(true);
    expect(marks.map((mark) => mark.side)).toEqual(forty.map((item) => item.side));
    expect(ideaLines(null, '1h')).toEqual([]);
  });

  // Scenarios «Только выбранная» and «Закрытая идея».
  it('draws only the entry and the exit of the chosen closed idea, with their text, and no lines', () => {
    expect(ideaMarks([...forty, shortClosed], '1h', shortClosed)).toEqual([
      {
        id: 'short-1',
        time: Date.UTC(2026, 10, 21, 8),
        kind: 'entry',
        side: 'short',
        text: '▼ шорт · funding-short',
      },
      {
        id: 'short-1',
        time: Date.UTC(2026, 10, 21, 12),
        kind: 'exit',
        side: 'short',
        text: 'выход по сроку +0.41 %',
      },
    ]);
    expect(ideaLines(shortClosed, '1h')).toEqual([]);
  });

  // Scenario «Открытая идея».
  it('draws the entry of the chosen open idea and its lines of the entry, the stop and the target to its term', () => {
    expect(ideaMarks([...forty, longOpen], '1h', longOpen)).toEqual([
      {
        id: 'long-1',
        time: thirteen,
        kind: 'entry',
        side: 'long',
        text: '▲ лонг · breakout-long',
      },
    ]);
    const to = Date.UTC(2026, 10, 22, 17);
    expect(ideaLines(longOpen, '1h')).toEqual([
      {
        id: 'long-1',
        kind: 'entry',
        price: 236.1,
        title: 'вход 236.10',
        from: thirteen,
        to,
        term: expect.stringMatching(/^срок \d\d:\d\d$/) as unknown as string,
      },
      { id: 'long-1', kind: 'stop', price: 231.38, title: 'стоп 231.38', from: thirteen, to },
      { id: 'long-1', kind: 'target', price: 245.54, title: 'цель 245.54', from: thirteen, to },
    ]);
  });

  // Scenario «Идея другого таймфрейма».
  it('puts the marks of an idea of 15m on the candle of 1h its moments fall in, chosen or not', () => {
    const squeeze = idea({
      id: 'squeeze',
      timeframe: '15m',
      moment: Date.UTC(2026, 10, 22, 14, 15),
      state: 'closed',
      exitTime: Date.UTC(2026, 10, 22, 15, 30),
      exitReason: 'target',
      result: 0.01,
    });
    expect(ideaMarks([squeeze], '1h', null).map((mark) => mark.time)).toEqual([
      Date.UTC(2026, 10, 22, 14),
    ]);
    expect(ideaMarks([squeeze], '1h', squeeze).map((mark) => mark.time)).toEqual([
      Date.UTC(2026, 10, 22, 14),
      Date.UTC(2026, 10, 22, 15),
    ]);
  });
});
