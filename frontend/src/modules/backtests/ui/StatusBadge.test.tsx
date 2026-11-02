import { render, screen } from '@testing-library/react';

import { StatusBadge } from './StatusBadge';

const progress = {
  loadedCandles: 1,
  expectedCandles: 2,
  calculatedCombinations: 5,
  totalCombinations: 9,
};

const dotOf = (text: string) =>
  (screen.getByText(text).querySelector('span') as HTMLElement).style.background;

describe('status badge', () => {
  it('colors the dot by the outcome or the work', () => {
    render(
      <>
        <StatusBadge status="Succeeded" progress={progress} />
        <StatusBadge status="Failed" progress={progress} />
        <StatusBadge status="Running" progress={progress} />
      </>,
    );

    expect(dotOf('Готов')).toBe('var(--color-ok)');
    expect(dotOf('Ошибка')).toBe('var(--color-err)');
    expect(dotOf('Считается · 5 / 9')).toBe('var(--color-run)');
  });

  it('gives a waiting or cancelled backtest a faint dot', () => {
    render(
      <>
        <StatusBadge status="Pending" progress={progress} />
        <StatusBadge status="Cancelled" progress={progress} />
      </>,
    );

    expect(dotOf('В очереди')).toBe('var(--color-faint)');
    expect(dotOf('Отменён · 5 / 9')).toBe('var(--color-faint)');
  });
});
