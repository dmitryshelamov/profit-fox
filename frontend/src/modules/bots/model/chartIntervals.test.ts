import { chartIntervals, shownInterval } from './chartIntervals';

describe('chartIntervals', () => {
  it('offers the fixed intervals and the interval of the bot marked as its own', () => {
    expect(chartIntervals('30')).toEqual([
      { value: '1', label: '1 мин', own: false },
      { value: '5', label: '5 мин', own: false },
      { value: '15', label: '15 мин', own: false },
      { value: '30', label: '30 мин · бот', own: true },
      { value: '60', label: '1 ч', own: false },
      { value: '240', label: '4 ч', own: false },
      { value: 'D', label: '1 д', own: false },
    ]);
  });

  it('marks the item of the list when the interval of the bot is already there', () => {
    const offered = chartIntervals('60');

    expect(offered.map((item) => item.value)).toEqual(['1', '5', '15', '60', '240', 'D']);
    expect(offered.find((item) => item.own)).toEqual({
      value: '60',
      label: '1 ч · бот',
      own: true,
    });
  });
});

describe('shownInterval', () => {
  it('takes the interval of the address when the chart offers it', () => {
    expect(shownInterval('D', '60')).toBe('D');
    expect(shownInterval('5', '30')).toBe('5');
  });

  it('falls back to the interval of the bot for an unknown or missing one', () => {
    expect(shownInterval('7', '60')).toBe('60');
    expect(shownInterval('3', '60')).toBe('60');
    expect(shownInterval(undefined, '60')).toBe('60');
  });
});
