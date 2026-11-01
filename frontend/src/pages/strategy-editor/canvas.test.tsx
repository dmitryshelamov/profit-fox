import { screen, within } from '@testing-library/react';

import catalog from '@/test/catalog.fixture.json';
import { fakeApi, json, problem } from '@/test/fakeApi';
import { emaSummary, ladderSummary, savedStrategy } from '@/test/fixtures';
import { renderApp } from '@/test/renderApp';

import ema from '../../../../backend/tests/ProfitFox.Tests.Shared/Fixtures/Strategies/ema.json';
import ladder from '../../../../backend/tests/ProfitFox.Tests.Shared/Fixtures/Strategies/ladder.json';

describe('strategy canvas', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // Scenarios "Эталонные стратегии открываются полностью" for EMA (the end-to-end test opens all four) and
  // "Сохранённые координаты".
  it('shows every node and wire of EMA, saved nodes at their positions', async () => {
    const definition = { ...ema, layout: { cross: { x: 240, y: 0 } } };
    fakeApi({
      [`GET /api/strategies/${emaSummary.id}`]: () => json(savedStrategy(emaSummary, definition)),
      'GET /api/strategy-nodes': () => json(catalog),
    });

    const { container } = renderApp(`/strategies/${emaSummary.id}`);

    for (const title of [
      'Параметры',
      'Свеча',
      'Позиция',
      'Пересечение EMA',
      'НЕ',
      'И',
      'Рыночный ордер',
      'Закрыть позицию',
    ]) {
      expect(await screen.findByRole('group', { name: title })).toBeInTheDocument();
    }
    expect(container.querySelectorAll('.react-flow__edge')).toHaveLength(9);
    expect(
      container.querySelector<HTMLElement>('.react-flow__node[data-id="cross"]')!.style.transform,
    ).toBe('translate(240px,0px)');
  });

  it('cuts a long pin name on the fixed-width node and gives it in full in a tooltip', async () => {
    fakeApi({
      [`GET /api/strategies/${ladderSummary.id}`]: () => json(savedStrategy(ladderSummary, ladder)),
      'GET /api/strategy-nodes': () => json(catalog),
    });

    renderApp(`/strategies/${ladderSummary.id}`);

    const parameters = await screen.findByRole('group', { name: 'Параметры' });
    expect(parameters).toHaveStyle({ width: '200px' });
    expect(within(parameters).getByTitle('priceRiseForSell')).toHaveClass('truncate');
  });

  // A long title would wrap and make the node taller than the layout counts.
  it('cuts a long node title to one line and gives it in full in a tooltip', async () => {
    fakeApi({
      [`GET /api/strategies/${emaSummary.id}`]: () => json(savedStrategy(emaSummary, ema)),
      'GET /api/strategy-nodes': () => json(catalog),
    });

    renderApp(`/strategies/${emaSummary.id}`);

    const cross = await screen.findByRole('group', { name: 'Пересечение EMA' });
    expect(within(cross).getByTitle('Пересечение EMA')).toHaveClass('truncate');
  });

  it('says so when there is no such strategy', async () => {
    fakeApi({
      [`GET /api/strategies/${emaSummary.id}`]: () => problem(404, 'Not Found'),
      'GET /api/strategy-nodes': () => json(catalog),
    });

    renderApp(`/strategies/${emaSummary.id}`);

    expect(await screen.findByText('Стратегия не найдена.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'К стратегиям' })).toBeInTheDocument();
  });
});
