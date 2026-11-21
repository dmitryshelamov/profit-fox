import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';

import { Dialog } from './Dialog';
import { Menu } from './Menu';
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
});

describe('Segmented', () => {
  function Timeframe() {
    const [value, setValue] = useState('4h');
    return (
      <Segmented
        label="Таймфрейм"
        value={value}
        options={['1h', '4h', '1d'].map((item) => ({ value: item, label: item }))}
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
    expect(checked()).toEqual(['1h']);
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

// Spec bench-ui «Цвет статуса прогона»: the word is always there, the color goes by the tone.
describe('RunStatusBadge', () => {
  it.each([
    ['passed', 'Прошла', 'ok'],
    ['insufficient_data', 'Данных мало', 'warn'],
    ['failed', 'Упала', 'err'],
    ['rejected', 'Отвергнута', 'neutral'],
    ['parity', 'Сверка', 'neutral'],
    ['cancelled', 'Отменён', 'neutral'],
    ['running', 'Идёт', 'run'],
    ['queued', 'В очереди', 'muted'],
  ])('shows %s as «%s» in the tone %s', (status, word, tone) => {
    render(<RunStatusBadge status={status} />);

    expect(screen.getByText(word)).toHaveAttribute('data-tone', tone);
  });

  // Scenario «Упавший и отвергнутый прогоны».
  it('tells a failed run from a rejected one by the tone, each with its word', () => {
    render(
      <>
        <RunStatusBadge status="failed" />
        <RunStatusBadge status="rejected" />
      </>,
    );

    expect(screen.getByText('Упала')).toHaveAttribute('data-tone', 'err');
    expect(screen.getByText('Отвергнута')).toHaveAttribute('data-tone', 'neutral');
  });
});
