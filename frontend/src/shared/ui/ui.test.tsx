import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';

import { timeframes } from '@/preview/preview';

import { Dialog } from './Dialog';
import { Menu } from './Menu';
import { PagedTable } from './PagedTable';
import { RunStatusBadge } from './RunStatusBadge';
import { Segmented } from './Segmented';
import { Tabs } from './Tabs';

describe('Menu', () => {
  function rowMenu(onSelect = vi.fn()) {
    render(
      <>
        <Menu
          label="Действия price-drop"
          items={[
            { label: 'Переименовать', onSelect: () => onSelect('rename') },
            { label: 'Удалить', danger: true, onSelect: () => onSelect('delete') },
          ]}
        />
        <p>мимо</p>
      </>,
    );
    return { onSelect, button: screen.getByRole('button', { name: 'Действия price-drop' }) };
  }

  it('opens by its button and lists its items', async () => {
    const { button } = rowMenu();
    expect(screen.queryByRole('menu')).toBeNull();
    expect(button).toHaveAttribute('aria-expanded', 'false');

    await userEvent.click(button);

    const menu = screen.getByRole('menu');
    expect(button).toHaveAttribute('aria-expanded', 'true');
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.textContent),
    ).toEqual(['Переименовать', 'Удалить']);
  });

  it('closes on Escape without an action and gives the focus back to its button', async () => {
    const { button, onSelect } = rowMenu();
    await userEvent.click(button);

    await userEvent.keyboard('{Escape}');

    expect(screen.queryByRole('menu')).toBeNull();
    expect(button).toHaveFocus();
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('closes on a click outside and gives the focus back to its button', async () => {
    const { button, onSelect } = rowMenu();
    await userEvent.click(button);

    await userEvent.click(screen.getByText('мимо'));

    expect(screen.queryByRole('menu')).toBeNull();
    expect(button).toHaveFocus();
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('runs the chosen item and closes; the arrows move between the items', async () => {
    const { button, onSelect } = rowMenu();
    await userEvent.click(button);
    expect(screen.getByRole('menuitem', { name: 'Переименовать' })).toHaveFocus();

    await userEvent.keyboard('{ArrowDown}');
    expect(screen.getByRole('menuitem', { name: 'Удалить' })).toHaveFocus();
    await userEvent.keyboard('{Enter}');

    expect(onSelect).toHaveBeenCalledExactlyOnceWith('delete');
    expect(screen.queryByRole('menu')).toBeNull();
  });
  it('leaves the focus where a click outside put it', async () => {
    render(
      <>
        <Menu label="Действия price-drop" items={[{ label: 'Удалить', onSelect: vi.fn() }]} />
        <input type="search" aria-label="Найти по имени" />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Действия price-drop' }));

    await userEvent.click(screen.getByRole('searchbox', { name: 'Найти по имени' }));
    await new Promise((resolve) => setTimeout(resolve));

    expect(screen.queryByRole('menu')).toBeNull();
    expect(screen.getByRole('searchbox', { name: 'Найти по имени' })).toHaveFocus();
  });

  it('gives way to the menu of another row, which keeps its keys', async () => {
    render(
      <>
        <Menu label="Действия price-drop" items={[{ label: 'Удалить', onSelect: vi.fn() }]} />
        <Menu label="Действия funding-high" items={[{ label: 'Копировать', onSelect: vi.fn() }]} />
      </>,
    );
    const other = screen.getByRole('button', { name: 'Действия funding-high' });
    await userEvent.click(screen.getByRole('button', { name: 'Действия price-drop' }));

    await userEvent.click(other);
    await new Promise((resolve) => setTimeout(resolve));

    expect(screen.getAllByRole('menu')).toHaveLength(1);
    expect(screen.getByRole('menuitem', { name: 'Копировать' })).toHaveFocus();
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('menu')).toBeNull();
    expect(other).toHaveFocus();
  });
});

describe('Dialog', () => {
  it('closes on Escape without the action', async () => {
    const onClose = vi.fn();
    const onDelete = vi.fn();
    render(
      <Dialog label="Удалить сигнал" onClose={onClose}>
        <p>Удалить сигнал price-drop?</p>
        <button type="button" onClick={onDelete}>
          Удалить
        </button>
      </Dialog>,
    );

    expect(screen.getByRole('dialog', { name: 'Удалить сигнал' })).toBeVisible();
    await userEvent.keyboard('{Escape}');

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(onDelete).not.toHaveBeenCalled();
  });

  it('gives the focus back to what opened it', async () => {
    function Host() {
      const [open, setOpen] = useState(false);
      return (
        <>
          <button type="button" onClick={() => setOpen(true)}>
            Открыть
          </button>
          {open && (
            <Dialog label="Вопрос" onClose={() => setOpen(false)}>
              <button type="button" autoFocus>
                Да
              </button>
            </Dialog>
          )}
        </>
      );
    }
    render(<Host />);
    const opener = screen.getByRole('button', { name: 'Открыть' });
    await userEvent.click(opener);
    expect(screen.getByRole('dialog')).toContainElement(document.activeElement as HTMLElement);

    await userEvent.keyboard('{Escape}');

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(opener).toHaveFocus();
  });
});

describe('Segmented', () => {
  function Timeframe() {
    const [value, setValue] = useState('4h');
    return (
      <Segmented
        label="Таймфрейм"
        value={value}
        options={timeframes.map((item) => ({ value: item, label: item }))}
        onChange={setValue}
      />
    );
  }

  const checked = () =>
    within(screen.getByRole('radiogroup', { name: 'Таймфрейм' }))
      .getAllByRole('radio')
      .filter((radio) => radio.getAttribute('aria-checked') === 'true')
      .map((radio) => radio.textContent);

  it('switches by a click', async () => {
    render(<Timeframe />);
    expect(checked()).toEqual(['4h']);

    await userEvent.click(screen.getByRole('radio', { name: '1h' }));

    expect(checked()).toEqual(['1h']);
  });

  it('switches by the arrows, around the ends', async () => {
    render(<Timeframe />);
    screen.getByRole('radio', { name: '4h' }).focus();

    await userEvent.keyboard('{ArrowRight}');
    expect(checked()).toEqual(['1d']);
    expect(screen.getByRole('radio', { name: '1d' })).toHaveFocus();
    await userEvent.keyboard('{ArrowRight}');
    expect(checked()).toEqual(['5m']);
    await userEvent.keyboard('{ArrowLeft}');
    expect(checked()).toEqual(['1d']);
  });
});

describe('Tabs', () => {
  function Lists() {
    const [value, setValue] = useState('trades');
    return (
      <Tabs
        label="Результат"
        value={value}
        tabs={[
          { value: 'trades', label: 'Сделки', count: 2 },
          { value: 'orders', label: 'Заявки', count: 3 },
        ]}
        onChange={setValue}
      />
    );
  }

  it('names a tab with its count and switches by a click', async () => {
    render(<Lists />);
    const trades = screen.getByRole('tab', { name: 'Сделки 2' });
    const orders = screen.getByRole('tab', { name: 'Заявки 3' });
    expect(trades).toHaveAttribute('aria-selected', 'true');

    await userEvent.click(orders);

    expect(orders).toHaveAttribute('aria-selected', 'true');
    expect(trades).toHaveAttribute('aria-selected', 'false');
  });

  it('switches by the arrows', async () => {
    render(<Lists />);
    screen.getByRole('tab', { name: 'Сделки 2' }).focus();

    await userEvent.keyboard('{ArrowRight}');

    expect(screen.getByRole('tab', { name: 'Заявки 3' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tab', { name: 'Заявки 3' })).toHaveFocus();
    await userEvent.keyboard('{ArrowLeft}');
    expect(screen.getByRole('tab', { name: 'Сделки 2' })).toHaveAttribute('aria-selected', 'true');
  });
});

// Spec bench-ui «Цвет состояния прогона»: the word is always there, the color goes by the tone.
describe('RunStatusBadge', () => {
  it.each([
    ['done', 'Готов', 'ok'],
    ['failed', 'Упал', 'err'],
    ['running', 'Идёт', 'run'],
    ['cancelled', 'Отменён', 'neutral'],
    ['queued', 'В очереди', 'neutral'],
  ])('shows %s as «%s» in the tone %s', (state, word, tone) => {
    render(<RunStatusBadge state={state} />);

    expect(screen.getByText(word)).toHaveAttribute('data-tone', tone);
  });

  // Scenario «Упавший и готовый прогоны».
  it('tells a failed run from a done one by the tone, each with its word', () => {
    render(
      <>
        <RunStatusBadge state="failed" />
        <RunStatusBadge state="done" />
      </>,
    );

    expect(screen.getByText('Упал')).toHaveAttribute('data-tone', 'err');
    expect(screen.getByText('Готов')).toHaveAttribute('data-tone', 'ok');
  });
});

// Task 18.23: the table scrolls to the chosen row when the choice changes, not on every answer of the server.
describe('the scroll of a paged table', () => {
  const columns = [{ key: 'name', title: 'Имя', cell: (row: { id: string }) => row.id }];
  const table = (rows: readonly { id: string }[], selected: string | null) => (
    <PagedTable
      label="Строки"
      columns={columns}
      rows={rows}
      sort={{ field: 'name', descending: false }}
      rowKey={(row) => row.id}
      onSort={() => undefined}
      selected={selected}
    />
  );

  it('scrolls once for a choice, again for another choice, and not for new rows', () => {
    const scroll = vi.fn();
    Element.prototype.scrollIntoView = scroll;
    const { rerender } = render(table([{ id: 'a' }, { id: 'b' }], 'b'));
    expect(scroll).toHaveBeenCalledTimes(1);

    rerender(table([{ id: 'a' }, { id: 'b' }], 'b'));
    rerender(table([{ id: 'a' }, { id: 'b' }, { id: 'c' }], 'b'));
    expect(scroll).toHaveBeenCalledTimes(1);

    rerender(table([{ id: 'a' }, { id: 'b' }, { id: 'c' }], 'a'));
    expect(scroll).toHaveBeenCalledTimes(2);
  });

  it('scrolls to a chosen row once it comes', () => {
    const scroll = vi.fn();
    Element.prototype.scrollIntoView = scroll;
    const { rerender } = render(table([], 'b'));
    expect(scroll).not.toHaveBeenCalled();

    rerender(table([{ id: 'a' }, { id: 'b' }], 'b'));
    expect(scroll).toHaveBeenCalledTimes(1);
  });
});

// Change add-idea-score, task 3.6: a table wider than its frame scrolls inside it, and the tables of ideas sit closer.
describe('the width of a paged table', () => {
  const columns = [{ key: 'name', title: 'Имя', cell: (row: { id: string }) => row.id }];
  const table = (dense: boolean) => (
    <PagedTable
      label="Строки"
      columns={columns}
      rows={[{ id: 'a' }]}
      sort={{ field: 'name', descending: false }}
      rowKey={(row) => row.id}
      onSort={() => undefined}
      dense={dense}
    />
  );

  it('scrolls a wide table inside its frame', () => {
    render(table(false));
    expect(screen.getByRole('table', { name: 'Строки' }).parentElement).toHaveClass(
      'overflow-x-auto',
    );
  });

  it('sets the cells of a dense table 14 px apart and leaves the others at 20', () => {
    const { unmount } = render(table(true));
    expect(screen.getByRole('table', { name: 'Строки' }).parentElement).toHaveClass(
      '[&_td]:pl-3.5',
      '[&_th]:pl-3.5',
    );
    unmount();
    render(table(false));
    expect(screen.getByRole('table', { name: 'Строки' }).parentElement).not.toHaveClass(
      '[&_td]:pl-3.5',
    );
  });
});
