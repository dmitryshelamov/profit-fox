import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { ideaOf } from '@/test/fixtures';

import { PairIdeasTable } from './PairIdeasTable';

const pullback = ideaOf({ id: 'idea-pullback', symbol: 'SOLUSDT', type: 'pullback' });
const levels = ideaOf({ id: 'idea-levels', symbol: 'SOLUSDT', type: 'levels', side: 'short' });
const expired = ['a', 'b', 'c'].map((id) =>
  ideaOf({
    id: `idea-expired-${id}`,
    symbol: 'SOLUSDT',
    type: 'beyond_extreme',
    status: 'expired',
  }),
);

const rowsOf = () =>
  screen
    .getAllByRole('row')
    .slice(1)
    .map((row) => row.textContent);

describe('the table of the ideas of a pair', () => {
  // Requirement "Чипы признаков в строке идеи": the chips stand after the setup, an idea without features has none.
  it('shows the chips of the features of an idea after its setup', () => {
    render(
      <PairIdeasTable
        open={[
          { ...pullback, features: ['day_drop', 'btc_drop'] },
          { ...levels, features: [] },
        ]}
        expired={[]}
        amount="10"
        selectedId={undefined}
        onSelect={vi.fn()}
      />,
    );

    const [marked, plain] = screen.getAllByRole('row').slice(1);
    const setup = within(marked!).getAllByRole('cell')[1]!;
    expect(within(setup).getByText('↓ пара')).toHaveAttribute(
      'title',
      'пара упала за сутки на 5 % и больше',
    );
    expect(within(setup).getByText('↓ BTC')).toBeInTheDocument();
    expect(within(plain!).queryByText(/↓|↑/)).not.toBeInTheDocument();
  });

  // Scenario "Колонки строки".
  it('has the columns of the ideas but the instrument', () => {
    render(
      <PairIdeasTable
        open={[pullback]}
        expired={[]}
        amount="10"
        selectedId={undefined}
        onSelect={vi.fn()}
      />,
    );

    expect(screen.getAllByRole('columnheader').map((header) => header.textContent)).toEqual([
      'Найдена',
      'Сетап',
      'ТФ',
      'Тип идеи',
      'Сторона',
      'Вход',
      'Стоп',
      'Цель',
      'R:R',
      'Вероятность',
      'Ожидание',
      'Прибыль / убыток',
      'Статус',
    ]);
    expect(screen.getByRole('columnheader', { name: 'Вход' })).toHaveClass('text-right');
    expect(screen.getByRole('columnheader', { name: 'Сетап' })).not.toHaveClass('text-right');
    expect(rowsOf()[0]).not.toContain('SOLUSDT');
    expect(rowsOf()[0]).not.toContain('linear');
  });

  // Scenario "Деньги в строке".
  it('shows the profit and the loss of the amount per idea', () => {
    render(
      <PairIdeasTable
        open={[pullback]}
        expired={[]}
        amount="10"
        selectedId={undefined}
        onSelect={vi.fn()}
      />,
    );

    const row = within(screen.getAllByRole('row')[1]!);

    expect(row.getByText('+0.39')).toBeInTheDocument();
    expect(row.getByText('−0.21')).toBeInTheDocument();
  });

  // Scenario "Выбор строки в таблице пары".
  it('selects an idea by a click on its row or on its found time', async () => {
    const onSelect = vi.fn();
    render(
      <PairIdeasTable
        open={[pullback, levels]}
        expired={[]}
        amount="10"
        selectedId={undefined}
        onSelect={onSelect}
      />,
    );

    await userEvent.click(within(screen.getAllByRole('row')[2]!).getByText('Уровни'));
    await userEvent.click(within(screen.getAllByRole('row')[1]!).getByRole('button'));

    expect(onSelect.mock.calls).toEqual([[levels], [pullback]]);
  });

  it('marks the selected idea', () => {
    render(
      <PairIdeasTable
        open={[pullback, levels]}
        expired={[]}
        amount="10"
        selectedId="idea-levels"
        onSelect={vi.fn()}
      />,
    );

    expect(
      screen.getAllByRole('button').map((button) => button.getAttribute('aria-pressed')),
    ).toEqual(['false', 'true']);
  });

  // Scenario "Порядок не меняется по заголовку".
  it('keeps the order of the rows when a header is clicked', async () => {
    render(
      <PairIdeasTable
        open={[pullback, levels]}
        expired={[]}
        amount="10"
        selectedId={undefined}
        onSelect={vi.fn()}
      />,
    );
    const before = rowsOf();

    await userEvent.click(screen.getByRole('columnheader', { name: 'Тип идеи' }));

    expect(rowsOf()).toEqual(before);
    expect(
      within(screen.getByRole('columnheader', { name: 'Тип идеи' })).queryByRole('button'),
    ).toBeNull();
  });

  describe('the expired ideas', () => {
    const toggle = () => screen.getByRole('button', { name: 'Истёкшие · 3' });
    const expiredRows = () => screen.queryAllByText('Истекла');

    // Scenario "Истёкшие свёрнуты".
    it('are a collapsed group with their number', () => {
      render(
        <PairIdeasTable
          open={[pullback, levels]}
          expired={expired}
          amount="10"
          selectedId={undefined}
          onSelect={vi.fn()}
        />,
      );

      expect(toggle()).toHaveAttribute('aria-expanded', 'false');
      expect(rowsOf()).toEqual([
        expect.stringContaining('Откат'),
        expect.stringContaining('Уровни'),
        'Истёкшие · 3',
      ]);
      expect(expiredRows()).toEqual([]);
    });

    // Scenario "Раскрытие по клику".
    it('open and close by a click', async () => {
      render(
        <PairIdeasTable
          open={[pullback]}
          expired={expired}
          amount="10"
          selectedId={undefined}
          onSelect={vi.fn()}
        />,
      );

      await userEvent.click(toggle());

      expect(toggle()).toHaveAttribute('aria-expanded', 'true');
      expect(expiredRows()).toHaveLength(3);
      expect(expiredRows()[0]!.closest('tr')).toHaveClass('opacity-70');
      expect(screen.getAllByRole('row')[1]).not.toHaveClass('opacity-70');

      await userEvent.click(toggle());

      expect(toggle()).toHaveAttribute('aria-expanded', 'false');
      expect(expiredRows()).toEqual([]);
    });

    // Scenario "Выбрана истёкшая идея".
    it('stay collapsed when an expired idea is selected', () => {
      render(
        <PairIdeasTable
          open={[pullback]}
          expired={expired}
          amount="10"
          selectedId="idea-expired-b"
          onSelect={vi.fn()}
        />,
      );

      expect(toggle()).toHaveAttribute('aria-expanded', 'false');
      expect(expiredRows()).toEqual([]);
    });

    it('select an idea of the opened group', async () => {
      const onSelect = vi.fn();
      render(
        <PairIdeasTable
          open={[pullback]}
          expired={expired}
          amount="10"
          selectedId={undefined}
          onSelect={onSelect}
        />,
      );
      await userEvent.click(toggle());

      await userEvent.click(expiredRows()[1]!);

      expect(onSelect).toHaveBeenCalledWith(expired[1]);
    });

    it('have no group when there is none', () => {
      render(
        <PairIdeasTable
          open={[pullback]}
          expired={[]}
          amount="10"
          selectedId={undefined}
          onSelect={vi.fn()}
        />,
      );

      expect(screen.queryByRole('button', { name: /Истёкшие/ })).not.toBeInTheDocument();
    });
  });
});
