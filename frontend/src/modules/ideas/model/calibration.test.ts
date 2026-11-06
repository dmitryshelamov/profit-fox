import { brierView, differenceView, rangeLabel } from './calibration';

describe('differenceView', () => {
  // Scenario "Завышенная корзина": −13 p.p. is more than twice the half-width 6 of 45–57 %.
  it('is red past two half-widths of the interval', () => {
    expect(differenceView({ promised: '0.64', actual: '0.51', low: '0.45', high: '0.57' })).toEqual(
      {
        text: '−13 п.п.',
        tone: 'err',
      },
    );
  });

  // Scenario "Честная корзина": the promise is inside the interval.
  it('is grey when the promise is inside the interval', () => {
    expect(differenceView({ promised: '0.35', actual: '0.34', low: '0.31', high: '0.37' })).toEqual(
      {
        text: '−1 п.п.',
        tone: 'muted',
      },
    );
  });

  it('is yellow past the interval but within two half-widths', () => {
    expect(differenceView({ promised: '0.40', actual: '0.47', low: '0.42', high: '0.52' })).toEqual(
      {
        text: '+7 п.п.',
        tone: 'warn',
      },
    );
  });

  it('counts a promise on a bound of the interval as inside it, and two half-widths as not past them', () => {
    expect(
      differenceView({ promised: '0.31', actual: '0.34', low: '0.31', high: '0.37' }).tone,
    ).toBe('muted');
    expect(
      differenceView({ promised: '0.40', actual: '0.50', low: '0.45', high: '0.55' }).tone,
    ).toBe('warn');
  });

  it('is grey without an interval, under 30 cases', () => {
    expect(differenceView({ promised: '0.64', actual: '0.20', low: null, high: null })).toEqual({
      text: '−44 п.п.',
      tone: 'muted',
    });
  });

  it('shows no sign for no difference', () => {
    expect(differenceView({ promised: '0.3', actual: '0.3', low: '0.25', high: '0.35' }).text).toBe(
      '0 п.п.',
    );
  });
});

describe('brierView', () => {
  // Scenario "Плитка Брайера".
  it('compares the Brier score with the score without buckets', () => {
    expect(brierView('0.207', '0.219')).toEqual({
      scores: '0.207 против 0.219',
      verdict: 'лучше на 5.5 %',
    });
  });

  it('says how much worse the buckets are', () => {
    expect(brierView('0.25', '0.2').verdict).toBe('хуже на 25 %');
  });

  // Scenario "Корзины не лучше общей доли".
  it('says the buckets are no better than the share of targets when the scores are equal', () => {
    expect(brierView('0.219', '0.219').verdict).toBe('не лучше общей доли целей');
  });

  it('rounds each score to three digits', () => {
    expect(brierView('0.20749', '0.2191').scores).toBe('0.207 против 0.219');
  });
});

describe('rangeLabel', () => {
  it('names a range of probability in percent', () => {
    expect(rangeLabel(null, '0.2', '%')).toBe('< 20 %');
    expect(rangeLabel('0.2', '0.3', '%')).toBe('20–30 %');
    expect(rangeLabel('0.6', null, '%')).toBe('≥ 60 %');
  });

  it('names a range of expectancy in R', () => {
    expect(rangeLabel(null, '-0.2', 'R')).toBe('< −0.2 R');
    expect(rangeLabel('-0.2', '0', 'R')).toBe('−0.2…0 R');
    expect(rangeLabel('0.4', null, 'R')).toBe('≥ 0.4 R');
  });
});
